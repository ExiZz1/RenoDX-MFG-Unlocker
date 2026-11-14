using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace RenoDXMfgUnlocker;

public sealed record BackupTransactionFile(string RelativeTargetPath, byte[] InstalledBytes, bool? ExpectedOriginalExists = null, string? ExpectedOriginalSha256 = null);
public sealed record BackupTransactionResult(string TransactionId, string Status, string TransactionDirectory);
public sealed record BackupRestoreFilePreview(string RelativeTargetPath, string Action, string CurrentStatus, string Message)
{
    public bool OriginalBackupVerified { get; init; }
}
public sealed record BackupRestorePreview(string TransactionId, string Status, bool ManifestVerified, bool CanRestore, string Message, IReadOnlyList<BackupRestoreFilePreview> Files);
public sealed record BackupTransactionInfo(string TransactionId, string Status, DateTimeOffset? CreatedUtc, bool ManifestVerified, bool CanRestore, string Message, IReadOnlyList<BackupRestoreFilePreview> Files);

/// <summary>
/// Standalone transaction/backup primitive. It is intentionally not connected to the UI or installer.
/// Callers must provide an explicit allowed root and relative target paths.
/// </summary>
public sealed class BackupRestoreService
{
    private const int ManifestFormatVersion = 2;
    private readonly string transactionStoreRoot;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public BackupRestoreService(string transactionStoreRoot) => this.transactionStoreRoot = Path.GetFullPath(transactionStoreRoot);

    public Task<BackupTransactionResult> ExecuteAsync(string allowedRoot, IReadOnlyList<BackupTransactionFile> files, CancellationToken cancellationToken = default) =>
        Task.Run(() => Execute(allowedRoot, files, cancellationToken, null, null, null), cancellationToken);

    public Task<BackupTransactionResult> ExecuteWithProgressAsync(string allowedRoot, IReadOnlyList<BackupTransactionFile> files, IProgress<int>? progress, CancellationToken cancellationToken = default) =>
        Task.Run(() => Execute(allowedRoot, files, cancellationToken, progress, null, null), cancellationToken);

    internal Task<BackupTransactionResult> ExecuteAsync(string allowedRoot, IReadOnlyList<BackupTransactionFile> files, Action<int>? beforeApply, Action<int>? afterApply, CancellationToken cancellationToken = default) =>
        Task.Run(() => Execute(allowedRoot, files, cancellationToken, null, beforeApply, afterApply), cancellationToken);
    internal Task<BackupTransactionResult> ExecuteGuardedAsync(string allowedRoot, IReadOnlyList<BackupTransactionFile> files, Action<int> beforeApply, IProgress<int>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Execute(allowedRoot, files, cancellationToken, progress, beforeApply, null), cancellationToken);

    public Task<BackupTransactionResult> RestoreAsync(string allowedRoot, string transactionId, CancellationToken cancellationToken = default) =>
        Task.Run(() => Restore(allowedRoot, transactionId, cancellationToken), cancellationToken);

    internal Task<BackupTransactionResult> RestoreSettingsHistoryAsync(string allowedRoot, string transactionId) =>
        Task.Run(() => Restore(allowedRoot, transactionId, CancellationToken.None, guardHistory: true));

    public IReadOnlyList<BackupTransactionInfo> ListTransactions(string allowedRoot)
    {
        var root = ValidateRoot(allowedRoot);
        if (IsSameOrChild(root, transactionStoreRoot) || IsSameOrChild(transactionStoreRoot, root))
            throw new InvalidDataException("The transaction store and allowed game root must be separate, non-overlapping directories.");
        EnsureSafeStoreRoot(create: false);
        var transactions = new List<BackupTransactionInfo>();
        foreach (var directory in Directory.EnumerateDirectories(transactionStoreRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var id = Path.GetFileName(directory);
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            try
            {
                var preview = PrepareRestore(root, id, CancellationToken.None);
                var created = ReadAndValidateManifest(directory, id).CreatedUtc;
                transactions.Add(new(id, preview.Status, created, preview.ManifestVerified, preview.CanRestore, preview.Message, preview.Files));
            }
            catch (Exception ex)
            {
                transactions.Add(new(id, "Invalid", null, false, false, ex.Message, Array.Empty<BackupRestoreFilePreview>()));
            }
        }
        return transactions.OrderByDescending(x => x.CreatedUtc).ToArray();
    }

    public BackupRestorePreview PreviewRestore(string allowedRoot, string transactionId, CancellationToken cancellationToken = default) =>
        PrepareRestore(ValidateRoot(allowedRoot), transactionId, cancellationToken);

    internal bool HasNewerOverlappingTransaction(string allowedRoot, string transactionId)
    {
        var root = ValidateRoot(allowedRoot);
        EnsureSafeStoreRoot(create: false);
        var selected = PreviewRestore(root, transactionId);
        var manifest = ReadAndValidateManifest(Path.Combine(transactionStoreRoot, transactionId), transactionId);
        var paths = selected.Files.Select(f => f.RelativeTargetPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Directory.EnumerateDirectories(transactionStoreRoot))
        {
            var id = Path.GetFileName(folder);
            if (id == transactionId || !Guid.TryParseExact(id,"N",out _)) continue;
            TransactionManifest newer;
            try
            {
                EnsureNoReparsePoints(transactionStoreRoot, Path.Combine(folder,"manifest.json"), includeLeaf: true);
                newer = ReadAndValidateManifest(folder,id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Security.Cryptography.CryptographicException or JsonException) { continue; }
            if (newer.AllowedRootSha256 == manifest.AllowedRootSha256 && newer.CreatedUtc > manifest.CreatedUtc &&
                newer.Status is ("Completed" or "InProgress" or "RollbackNeedsReview") &&
                newer.Entries.Any(e => paths.Contains(e.RelativeTargetPath))) return true;
        }
        return false;
    }

    private BackupTransactionResult Execute(string allowedRoot, IReadOnlyList<BackupTransactionFile> files, CancellationToken token, IProgress<int>? progress, Action<int>? beforeApply, Action<int>? afterApply)
    {
        var root = ValidateRoot(allowedRoot);
        using var rootOperation = RootOperation.Enter(root);
        if (IsSameOrChild(root, transactionStoreRoot) || IsSameOrChild(transactionStoreRoot, root))
            throw new InvalidDataException("The transaction store and allowed game root must be separate, non-overlapping directories.");
        if (files is null || files.Count == 0) throw new InvalidDataException("A transaction must contain at least one file.");
        if (files.Any(f => f is null || f.InstalledBytes is null)) throw new InvalidDataException("A transaction file or its replacement bytes are missing.");
        var normalized = files.Select(f => (File: new BackupTransactionFile(f.RelativeTargetPath, (byte[])f.InstalledBytes.Clone(), f.ExpectedOriginalExists, f.ExpectedOriginalSha256), Relative: NormalizeRelative(f.RelativeTargetPath))).ToArray();
        if (normalized.Select(x => x.Relative).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
            throw new InvalidDataException("The transaction contains duplicate target paths.");
        EnsureSafeStoreRoot(create: true);

        var reservation = CreateTransactionDirectory();
        var id = reservation.Id;
        var transactionDirectory = reservation.Directory;
        var manifest = new TransactionManifest { FormatVersion = ManifestFormatVersion, TransactionId = id, AllowedRootSha256 = Hash(System.Text.Encoding.UTF8.GetBytes(root.ToUpperInvariant())), CreatedUtc = DateTimeOffset.UtcNow, Status = "Preparing" };
        SaveManifest(transactionDirectory, manifest);
        var applied = new List<TransactionEntry>();
        try
        {
            for (var i = 0; i < normalized.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var (file, relative) = normalized[i];
                var target = ResolveTarget(root, relative, createParent: false);
                EnsureNoReparsePoints(root, target, includeLeaf: true);
                byte[]? original = File.Exists(target) ? File.ReadAllBytes(target) : null;
                if (file.ExpectedOriginalExists is bool expectedExists)
                {
                    if (expectedExists != (original is not null))
                        throw new IOException($"Target '{relative}' appeared or disappeared after the installation preview; no write was made.");
                    if (expectedExists && (file.ExpectedOriginalSha256 is not { Length: 64 } expectedHash || Hash(original!) != expectedHash))
                        throw new IOException($"Target '{relative}' changed after the installation preview; no write was made.");
                    if (!expectedExists && file.ExpectedOriginalSha256 is not null)
                        throw new InvalidDataException($"New target '{relative}' unexpectedly included an existing-file hash.");
                }
                var entry = new TransactionEntry
                {
                    RelativeTargetPath = relative,
                    HadOriginal = original is not null,
                    OriginalSha256 = original is null ? null : Hash(original),
                    BackupCreatedUtc = original is null ? null : DateTimeOffset.UtcNow,
                    InstalledSha256 = Hash(file.InstalledBytes),
                    OriginalBackupPath = original is null ? null : $"originals/{i:D4}.bin",
                    InstalledSnapshotPath = $"installed/{i:D4}.bin",
                    Status = "Prepared"
                };
                if (original is not null) WriteNewSnapshot(transactionDirectory, entry.OriginalBackupPath!, original);
                WriteNewSnapshot(transactionDirectory, entry.InstalledSnapshotPath, file.InstalledBytes);
                manifest.Entries.Add(entry);
            }
            manifest.Status = "InProgress";
            SaveManifest(transactionDirectory, manifest);

            for (var i = 0; i < normalized.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                beforeApply?.Invoke(i);
                var entry = manifest.Entries[i];
                var target = ResolveTarget(root, entry.RelativeTargetPath, createParent: true);
                EnsureNoReparsePoints(root, target, includeLeaf: true);
                var targetExists = File.Exists(target) || Directory.Exists(target);
                if (entry.HadOriginal)
                {
                    if (!File.Exists(target) || HashFile(target) != entry.OriginalSha256)
                        throw new IOException($"Target '{entry.RelativeTargetPath}' changed after transaction preparation; no replacement was made.");
                }
                else if (targetExists)
                    throw new IOException($"Target '{entry.RelativeTargetPath}' appeared after transaction preparation; it was left untouched.");
                entry.Status = "Applying";
                SaveManifest(transactionDirectory, manifest);
                AtomicWrite(target, normalized[i].File.InstalledBytes);
                applied.Add(entry);
                if (!HashFile(target).Equals(entry.InstalledSha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Target '{entry.RelativeTargetPath}' did not retain the transaction's installed SHA-256; rollback will not overwrite any divergent bytes.");
                afterApply?.Invoke(i);
                entry.Status = "Installed";
                SaveManifest(transactionDirectory, manifest);
                progress?.Report((i + 1) * 100 / normalized.Length);
            }
            manifest.Status = "Completed";
            SaveManifest(transactionDirectory, manifest);
            return new(id, manifest.Status, transactionDirectory);
        }
        catch (Exception operationError)
        {
            var rollbackErrors = RollbackApplied(root, transactionDirectory, manifest, applied);
            manifest.Status = rollbackErrors.Count == 0 ? "RolledBack" : "RollbackNeedsReview";
            try { SaveManifest(transactionDirectory, manifest); } catch (Exception saveError) { rollbackErrors.Add("Could not save rollback status: " + saveError.Message); }
            if (rollbackErrors.Count != 0)
                throw new BackupTransactionException($"Transaction {id} failed ({operationError.Message}) and rollback needs review: {string.Join("; ", rollbackErrors)}", id, operationError);
            throw new BackupTransactionException($"Transaction {id} failed and all earlier changes were rolled back: {operationError.Message}", id, operationError);
        }
    }

    internal const string NewerHistory = "Restore newer changes to these configuration files first.";

    private BackupTransactionResult Restore(string allowedRoot, string transactionId, CancellationToken token, bool guardHistory = false)
    {
        var root = ValidateRoot(allowedRoot);
        using var rootOperation = RootOperation.Enter(root);
        if (guardHistory && HasNewerOverlappingTransaction(root,transactionId))
            throw new InvalidOperationException(NewerHistory);
        var plan = PrepareRestore(root, transactionId, token);
        if (!plan.ManifestVerified) throw new InvalidDataException(plan.Message);
        if (!plan.CanRestore)
        {
            if (plan.Files.All(x => x.Action is "Already restored" or "Already absent"))
            {
                var existingDirectory = Path.Combine(transactionStoreRoot, transactionId);
                var alreadyRestoredManifest = ReadAndValidateManifest(existingDirectory, transactionId);
                alreadyRestoredManifest.Status = "RolledBack";
                foreach (var entry in alreadyRestoredManifest.Entries) entry.Status = "Restored";
                SaveManifest(existingDirectory, alreadyRestoredManifest);
                return new(transactionId, "RolledBack", existingDirectory);
            }
            throw new IOException(plan.Message);
        }
        var directory = Path.Combine(transactionStoreRoot, transactionId);
        var manifest = ReadAndValidateManifest(directory, transactionId);
        var restoreActions = plan.Files.Where(x => x.Action is "Restore original" or "Remove transaction file")
            .Select(file =>
            {
                var entry = manifest.Entries.Single(x => x.RelativeTargetPath.Equals(file.RelativeTargetPath, StringComparison.OrdinalIgnoreCase));
                var target = ResolveTarget(root, entry.RelativeTargetPath, createParent: false);
                var original = entry.HadOriginal ? ReadVerifiedSnapshot(directory, entry.OriginalBackupPath!, entry.OriginalSha256!) : null;
                var installed = ReadVerifiedSnapshot(directory, entry.InstalledSnapshotPath, entry.InstalledSha256);
                return (Entry: entry, Target: target, Original: original, Installed: installed);
            }).ToList();
        if (manifest.Status == "RolledBack" && restoreActions.Count > 0)
            throw new InvalidDataException("The manifest claims rollback completed, but target files are not in the original state.");

        var restored = new List<(TransactionEntry Entry, string Target, byte[] Installed)>();
        try
        {
            foreach (var action in restoreActions)
            {
                token.ThrowIfCancellationRequested();
                EnsureNoReparsePoints(root, action.Target, includeLeaf: true);
                if (!File.Exists(action.Target) || HashFile(action.Target) != action.Entry.InstalledSha256)
                    throw new IOException($"Restore stopped: '{action.Entry.RelativeTargetPath}' changed during preflight; it was left untouched.");
                if (action.Entry.HadOriginal) AtomicWrite(action.Target, action.Original!);
                else File.Delete(action.Target);
                action.Entry.Status = "Restored";
                restored.Add((action.Entry, action.Target, action.Installed));
                SaveManifest(directory, manifest);
            }
            manifest.Status = "RolledBack";
            SaveManifest(directory, manifest);
            return new(transactionId, manifest.Status, directory);
        }
        catch (Exception restoreError)
        {
            var undoErrors = new List<string>();
            foreach (var action in restored.AsEnumerable().Reverse())
            {
                try
                {
                    EnsureNoReparsePoints(root, action.Target, includeLeaf: true);
                    if (action.Entry.HadOriginal)
                    {
                        if (!File.Exists(action.Target) || HashFile(action.Target) != action.Entry.OriginalSha256)
                            throw new IOException("Target changed after restore began; installed bytes were not put back over it.");
                    }
                    else if (File.Exists(action.Target) || Directory.Exists(action.Target))
                        throw new IOException("A file appeared after restore began; installed bytes were not put back over it.");
                    AtomicWrite(action.Target, action.Installed);
                    action.Entry.Status = "Installed";
                }
                catch (Exception ex) { undoErrors.Add($"{action.Entry.RelativeTargetPath}: {ex.Message}"); }
            }
            manifest.Status = undoErrors.Count == 0 ? "Completed" : "RollbackNeedsReview";
            try { SaveManifest(directory, manifest); } catch (Exception ex) { undoErrors.Add("Could not save manifest: " + ex.Message); }
            throw new BackupTransactionException(undoErrors.Count == 0
                ? $"Restore failed and the transaction's installed files were reinstated: {restoreError.Message}"
                : $"Restore failed and recovery needs review: {string.Join("; ", undoErrors)}", transactionId, restoreError);
        }
    }

    private BackupRestorePreview PrepareRestore(string allowedRoot, string transactionId, CancellationToken token)
    {
        if (IsSameOrChild(allowedRoot, transactionStoreRoot) || IsSameOrChild(transactionStoreRoot, allowedRoot))
            throw new InvalidDataException("The transaction store and allowed game root must be separate, non-overlapping directories.");
        if (!Guid.TryParseExact(transactionId, "N", out _)) throw new InvalidDataException("The transaction identifier is invalid.");
        EnsureSafeStoreRoot(create: false);
        var directory = Path.GetFullPath(Path.Combine(transactionStoreRoot, transactionId));
        if (!IsChild(transactionStoreRoot, directory)) throw new InvalidDataException("The transaction path is outside the transaction store.");
        EnsureNoReparsePoints(transactionStoreRoot, directory, includeLeaf: true);
        var ownerPath = Path.Combine(directory, ".owner");
        EnsureNoReparsePoints(directory, ownerPath, includeLeaf: true);
        if (!File.Exists(ownerPath) || !File.ReadAllText(ownerPath).Equals(transactionId, StringComparison.Ordinal))
            throw new InvalidDataException("The transaction directory reservation does not match its identifier.");
        var manifestPath = Path.Combine(directory, "manifest.json");
        EnsureNoReparsePoints(directory, manifestPath, includeLeaf: true);
        var manifest = ReadAndValidateManifest(directory, transactionId);
        var rootHash = Hash(System.Text.Encoding.UTF8.GetBytes(allowedRoot.ToUpperInvariant()));
        if (!manifest.AllowedRootSha256.Equals(rootHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This transaction belongs to a different allowed root; no files were changed.");
        if (manifest.Status is not ("Completed" or "RollbackNeedsReview" or "InProgress" or "RolledBack"))
            throw new InvalidDataException($"Transaction status '{manifest.Status}' is not eligible for restore.");

        var items = new List<BackupRestoreFilePreview>();
        foreach (var entry in manifest.Entries.AsEnumerable().Reverse())
        {
            token.ThrowIfCancellationRequested();
            var target = ResolveTarget(allowedRoot, entry.RelativeTargetPath, createParent: false);
            EnsureNoReparsePoints(allowedRoot, target, includeLeaf: true);
            if (entry.HadOriginal)
                _ = ReadVerifiedSnapshot(directory, entry.OriginalBackupPath ?? throw new InvalidDataException("Original backup path is missing."), entry.OriginalSha256 ?? throw new InvalidDataException("Original hash is missing."));
            void Add(string action, string state, string message) => items.Add(new(entry.RelativeTargetPath, action, state, message)
                { OriginalBackupVerified = entry.HadOriginal });

            var exists = File.Exists(target) || Directory.Exists(target);
            if (!exists)
            {
                if (entry.HadOriginal)
                    Add("Blocked", "Missing", "The original target is missing, so its state cannot be safely restored.");
                else
                    Add("Already absent", "Missing", "The transaction-created file is already absent.");
                continue;
            }
            if (!File.Exists(target))
            {
                Add("Blocked", "Directory or non-file target", "The target path is not a regular file.");
                continue;
            }
            var currentHash = HashFile(target);
            if (entry.HadOriginal && currentHash.Equals(entry.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                Add("Already restored", "Original bytes", "The target already matches the saved original.");
                continue;
            }
            if (!currentHash.Equals(entry.InstalledSha256, StringComparison.OrdinalIgnoreCase))
            {
                Add("Blocked", "Changed since transaction", "Current SHA-256 matches neither the original nor the transaction-installed file. It will be left untouched.");
                continue;
            }
            if (manifest.Status == "RolledBack")
            {
                Add("Blocked", "Unexpected installed bytes", "The manifest says this transaction was rolled back, but the target still matches its installed hash.");
                continue;
            }
            _ = ReadVerifiedSnapshot(directory, entry.InstalledSnapshotPath, entry.InstalledSha256);
            Add(entry.HadOriginal ? "Restore original" : "Remove transaction file", "Matches installed SHA-256", "Will restore only after the final per-file hash check.");
        }

        var hasBlocked = items.Any(x => x.Action == "Blocked");
        var changeCount = items.Count(x => x.Action is "Restore original" or "Remove transaction file");
        var canRestore = manifest.Status != "RolledBack" && !hasBlocked && changeCount > 0;
        var message = hasBlocked
            ? "Restore is blocked. " + string.Join(" ", items.Where(x => x.Action == "Blocked").Select(x => $"{x.RelativeTargetPath}: {x.Message}")) + " No files have been changed."
            : changeCount == 0
                ? "No files need restoring. No files have been changed."
                : $"Ready to restore {changeCount} file(s). No files have been changed yet.";
        return new(transactionId, manifest.Status, true, canRestore, message, items);
    }

    private List<string> RollbackApplied(string root, string transactionDirectory, TransactionManifest manifest, List<TransactionEntry> applied)
    {
        var errors = new List<string>();
        foreach (var entry in applied.AsEnumerable().Reverse())
        {
            try
            {
                var target = ResolveTarget(root, entry.RelativeTargetPath, createParent: false);
                EnsureNoReparsePoints(root, target, includeLeaf: true);
                if (!File.Exists(target) || HashFile(target) != entry.InstalledSha256)
                    throw new IOException("The target no longer matches the transaction-installed hash; it was left untouched.");
                if (entry.HadOriginal)
                {
                    var original = ReadVerifiedSnapshot(transactionDirectory, entry.OriginalBackupPath!, entry.OriginalSha256!);
                    AtomicWrite(target, original);
                }
                else File.Delete(target);
                entry.Status = "Restored";
                SaveManifest(transactionDirectory, manifest);
            }
            catch (Exception ex) { errors.Add($"{entry.RelativeTargetPath}: {ex.Message}"); }
        }
        return errors;
    }

    private TransactionManifest ReadAndValidateManifest(string directory, string expectedId)
    {
        TransactionManifest manifest;
        try { manifest = JsonSerializer.Deserialize<TransactionManifest>(File.ReadAllText(Path.Combine(directory, "manifest.json")), JsonOptions) ?? throw new InvalidDataException("Manifest is empty."); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { throw new InvalidDataException("The transaction manifest is unreadable or corrupted.", ex); }
        if (manifest.FormatVersion != ManifestFormatVersion || manifest.TransactionId != expectedId || !Guid.TryParseExact(manifest.TransactionId, "N", out _))
            throw new InvalidDataException("The manifest format or transaction identifier is invalid.");
        VerifyManifestAuthentication(manifest);
        ValidateHash(manifest.AllowedRootSha256);
        if (manifest.Status is not ("Preparing" or "InProgress" or "Completed" or "RolledBack" or "RollbackNeedsReview")) throw new InvalidDataException("The manifest status is invalid.");
        if (manifest.Entries is null || manifest.Entries.Count == 0 || manifest.Entries.Any(x => x is null) || manifest.Entries.Select(x => NormalizeRelative(x.RelativeTargetPath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Entries.Count)
            throw new InvalidDataException("The manifest contains no entries or duplicate target paths.");
        for (var index = 0; index < manifest.Entries.Count; index++)
        {
            var entry = manifest.Entries[index];
            entry.RelativeTargetPath = NormalizeRelative(entry.RelativeTargetPath);
            if (entry.Status is not ("Prepared" or "Applying" or "Installed" or "Restored")) throw new InvalidDataException("A manifest entry status is invalid.");
            ValidateHash(entry.InstalledSha256);
            if (entry.HadOriginal) { ValidateHash(entry.OriginalSha256); ValidateSnapshotPath(entry.OriginalBackupPath); if (entry.BackupCreatedUtc is null) throw new InvalidDataException("The original backup timestamp is missing."); }
            else if (entry.OriginalSha256 is not null || entry.OriginalBackupPath is not null || entry.BackupCreatedUtc is not null) throw new InvalidDataException("A new-file entry has unexpected original backup metadata.");
            ValidateSnapshotPath(entry.InstalledSnapshotPath);
            var expectedOrdinal = index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
            if (entry.InstalledSnapshotPath != $"installed/{expectedOrdinal}.bin" || (entry.HadOriginal && entry.OriginalBackupPath != $"originals/{expectedOrdinal}.bin"))
                throw new InvalidDataException("A manifest snapshot path does not match its transaction entry.");
        }
        return manifest;
    }

    private static byte[] ReadVerifiedSnapshot(string transactionDirectory, string relativePath, string expectedHash)
    {
        ValidateSnapshotPath(relativePath);
        var path = Path.GetFullPath(Path.Combine(transactionDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsChild(transactionDirectory, path)) throw new InvalidDataException("A snapshot path escapes the transaction directory.");
        EnsureNoReparsePoints(transactionDirectory, path, includeLeaf: true);
        var bytes = File.ReadAllBytes(path);
        if (Hash(bytes) != expectedHash) throw new InvalidDataException("A transaction backup snapshot does not match its manifest hash.");
        return bytes;
    }

    private void SaveManifest(string directory, TransactionManifest manifest)
    {
        EnsureNoReparsePoints(transactionStoreRoot, directory, includeLeaf: true);
        var path = Path.Combine(directory, "manifest.json");
        EnsureNoReparsePoints(directory, path, includeLeaf: true);
        var temp = Path.Combine(directory, $".manifest.{Guid.NewGuid():N}.tmp");
        manifest.IntegrityHmacSha256 = null;
        manifest.IntegrityHmacSha256 = ComputeManifestHmac(manifest, GetManifestKey(create: true));
        File.WriteAllText(temp, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    private void VerifyManifestAuthentication(TransactionManifest manifest)
    {
        var provided = manifest.IntegrityHmacSha256;
        if (provided is not { Length: 64 } || !provided.All(Uri.IsHexDigit)) throw new InvalidDataException("The manifest authentication tag is missing or malformed.");
        manifest.IntegrityHmacSha256 = null;
        var expected = ComputeManifestHmac(manifest, GetManifestKey(create: false));
        manifest.IntegrityHmacSha256 = provided;
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(provided), Convert.FromHexString(expected)))
            throw new InvalidDataException("The manifest was altered or does not match the protected transaction key.");
    }

    private static string ComputeManifestHmac(TransactionManifest manifest, byte[] key)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(bytes)).ToLowerInvariant();
    }

    private byte[] GetManifestKey(bool create)
    {
        var path = Path.Combine(transactionStoreRoot, ".manifest-key.dpapi");
        EnsureNoReparsePoints(transactionStoreRoot, path, includeLeaf: true);
        if (File.Exists(path))
        {
            try
            {
                var protectedKey = File.ReadAllBytes(path);
                var key = DpapiUnprotect(protectedKey);
                if (key.Length != 32) throw new InvalidDataException("The protected transaction key has an invalid length.");
                return key;
            }
            catch (Exception ex) when (ex is CryptographicException or Win32Exception or InvalidDataException)
            {
                throw new InvalidDataException("The protected transaction key is unavailable or corrupted; restore was refused.", ex);
            }
        }
        if (!create) throw new InvalidDataException("The protected transaction key is missing; the manifest cannot be authenticated.");

        var generated = RandomNumberGenerator.GetBytes(32);
        var protectedBytes = DpapiProtect(generated);
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            stream.Write(protectedBytes);
            stream.Flush(true);
            return generated;
        }
        catch (IOException) when (File.Exists(path))
        {
            CryptographicOperations.ZeroMemory(generated);
            return GetManifestKey(create: false);
        }
    }

    private static readonly byte[] DpapiEntropy = System.Text.Encoding.UTF8.GetBytes("RenoDX.MFG.Unlocker.BackupRestore.Manifest.v2");

    private static byte[] DpapiProtect(byte[] value) => DpapiTransform(value, protect: true);
    private static byte[] DpapiUnprotect(byte[] value) => DpapiTransform(value, protect: false);

    private static byte[] DpapiTransform(byte[] value, bool protect)
    {
        var input = ToBlob(value);
        var entropy = ToBlob(DpapiEntropy);
        DataBlob output = default;
        IntPtr description = IntPtr.Zero;
        try
        {
            var success = protect
                ? CryptProtectData(ref input, "RenoDX Backup Manifest Integrity Key", ref entropy, IntPtr.Zero, IntPtr.Zero, 0x1, out output)
                : CryptUnprotectData(ref input, out description, ref entropy, IntPtr.Zero, IntPtr.Zero, 0x1, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
            var bytes = new byte[output.Size];
            Marshal.Copy(output.Data, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            if (input.Data != IntPtr.Zero) Marshal.FreeHGlobal(input.Data);
            if (entropy.Data != IntPtr.Zero) Marshal.FreeHGlobal(entropy.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            if (description != IntPtr.Zero) LocalFree(description);
        }
    }

    private static DataBlob ToBlob(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DataBlob { Size = bytes.Length, Data = pointer };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public IntPtr Data; }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, out IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static void WriteNewSnapshot(string transactionDirectory, string relativePath, byte[] bytes)
    {
        var path = Path.Combine(transactionDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        EnsureNoReparsePoints(transactionDirectory, path, includeLeaf: true);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(true);
    }

    private static void AtomicWrite(string target, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(target)!;
        var temp = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.rdx-tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            if (File.Exists(target)) File.Move(temp, target, overwrite: true);
            else File.Move(temp, target);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }

    private string ValidateRoot(string allowedRoot)
    {
        if (string.IsNullOrWhiteSpace(allowedRoot) || !Directory.Exists(allowedRoot)) throw new DirectoryNotFoundException("The explicitly allowed game root does not exist.");
        var root = Path.GetFullPath(allowedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        EnsureNoReparsePoints(Path.GetPathRoot(root)!, root, includeLeaf: true);
        return root;
    }

    private string ResolveTarget(string root, string relative, bool createParent)
    {
        var normalized = NormalizeRelative(relative);
        var target = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsChild(root, target)) throw new InvalidDataException("A target path escapes the explicitly allowed root.");
        var parent = Path.GetDirectoryName(target)!;
        if (createParent) EnsureSafeDirectories(root, parent, create: true);
        else EnsureNoReparsePoints(root, parent, includeLeaf: true);
        return target;
    }

    private void EnsureSafeStoreRoot(bool create)
    {
        EnsureNoReparsePoints(Path.GetPathRoot(transactionStoreRoot)!, transactionStoreRoot, includeLeaf: true);
        if (create) Directory.CreateDirectory(transactionStoreRoot);
        if (!Directory.Exists(transactionStoreRoot)) throw new DirectoryNotFoundException("The transaction store does not exist.");
        EnsureNoReparsePoints(Path.GetPathRoot(transactionStoreRoot)!, transactionStoreRoot, includeLeaf: true);
    }

    private (string Id, string Directory) CreateTransactionDirectory()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var id = Guid.NewGuid().ToString("N");
            var directory = Path.Combine(transactionStoreRoot, id);
            if (Directory.Exists(directory) || File.Exists(directory)) continue;
            Directory.CreateDirectory(directory);
            EnsureNoReparsePoints(transactionStoreRoot, directory, includeLeaf: true);
            try
            {
                using var reservation = new FileStream(Path.Combine(directory, ".owner"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
                var bytes = System.Text.Encoding.ASCII.GetBytes(id);
                reservation.Write(bytes);
                reservation.Flush(true);
                return (id, directory);
            }
            catch (IOException)
            {
                // Preserve any directory that won the race; never delete an unowned transaction folder.
            }
        }
        throw new IOException("Could not allocate a unique transaction backup directory without overwriting earlier transactions.");
    }

    private static void EnsureSafeDirectories(string root, string directory, bool create)
    {
        var relative = Path.GetRelativePath(root, directory);
        var current = root;
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                if (!create) throw new DirectoryNotFoundException(current);
                Directory.CreateDirectory(current);
            }
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                throw new InvalidDataException("A target directory contains a reparse point or non-directory path segment.");
        }
    }

    private static void EnsureNoReparsePoints(string root, string path, bool includeLeaf)
    {
        var canonicalRoot = CanonicalPath(root);
        var canonicalPath = CanonicalPath(path);
        if (!canonicalPath.Equals(canonicalRoot, StringComparison.OrdinalIgnoreCase) && !IsChild(canonicalRoot, canonicalPath))
            throw new InvalidDataException("A path escapes its permitted root.");
        CheckReparse(canonicalRoot);
        var relative = Path.GetRelativePath(canonicalRoot, canonicalPath);
        var segments = relative == "." ? Array.Empty<string>() : relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var current = canonicalRoot;
        for (var i = 0; i < segments.Length; i++)
        {
            current = Path.Combine(current, segments[i]);
            if (!includeLeaf && i == segments.Length - 1) break;
            if (File.Exists(current) || Directory.Exists(current)) CheckReparse(current);
        }
    }

    private static void CheckReparse(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Reparse points are not allowed in transaction paths: '{path}'.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static string NormalizeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':')) throw new InvalidDataException("Target paths must be relative to the allowed root.");
        var segments = path.Replace('\\', '/').Split('/');
        if (segments.Any(s => s is "" or "." or "..")) throw new InvalidDataException("Target paths cannot contain empty, '.' or '..' segments.");
        foreach (var segment in segments)
        {
            if (segment.EndsWith(' ') || segment.EndsWith('.') || segment.Any(c => char.IsControl(c) || "<>\"|?*".Contains(c)))
                throw new InvalidDataException("A target path contains a Windows-invalid or ambiguous filename segment.");
            var deviceStem = segment.Split('.')[0];
            if (deviceStem.Equals("CON", StringComparison.OrdinalIgnoreCase) || deviceStem.Equals("PRN", StringComparison.OrdinalIgnoreCase) || deviceStem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || deviceStem.Equals("NUL", StringComparison.OrdinalIgnoreCase) || deviceStem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || deviceStem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) || IsNumberedDevice(deviceStem))
                throw new InvalidDataException("Device names are not allowed as target path segments.");
        }
        return string.Join('/', segments);
    }

    private static bool IsNumberedDevice(string stem) =>
        (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
        && stem.Length == 4 && stem[3] is >= '1' and <= '9';

    private static void ValidateSnapshotPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':')) throw new InvalidDataException("A manifest snapshot path is invalid.");
        var segments = path.Replace('\\', '/').Split('/');
        if (segments.Any(s => s is "" or "." or "..") || !(segments[0] is "originals" or "installed")) throw new InvalidDataException("A manifest snapshot path escapes its transaction directory.");
    }

    private static void ValidateHash(string? hash)
    {
        if (hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("A manifest SHA-256 value is invalid.");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string CanonicalPath(string path)
    {
        var full = Path.GetFullPath(path);
        var volumeRoot = Path.GetPathRoot(full)!;
        return full.Equals(volumeRoot, StringComparison.OrdinalIgnoreCase) ? full : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
    private static bool IsChild(string root, string path)
    {
        var canonicalRoot = CanonicalPath(root);
        var canonicalPath = CanonicalPath(path);
        var prefix = canonicalRoot.EndsWith(Path.DirectorySeparatorChar) || canonicalRoot.EndsWith(Path.AltDirectorySeparatorChar)
            ? canonicalRoot : canonicalRoot + Path.DirectorySeparatorChar;
        return canonicalPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
    private static bool IsSameOrChild(string root, string path) => CanonicalPath(root).Equals(CanonicalPath(path), StringComparison.OrdinalIgnoreCase) || IsChild(root, path);

    private sealed class TransactionManifest
    {
        public int FormatVersion { get; set; }
        public string TransactionId { get; set; } = "";
        public string AllowedRootSha256 { get; set; } = "";
        public string? IntegrityHmacSha256 { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
        public string Status { get; set; } = "Preparing";
        public List<TransactionEntry> Entries { get; set; } = [];
    }

    private sealed class TransactionEntry
    {
        public string RelativeTargetPath { get; set; } = "";
        public bool HadOriginal { get; set; }
        public string? OriginalSha256 { get; set; }
        public DateTimeOffset? BackupCreatedUtc { get; set; }
        public string InstalledSha256 { get; set; } = "";
        public string? OriginalBackupPath { get; set; }
        public string InstalledSnapshotPath { get; set; } = "";
        public string Status { get; set; } = "Prepared";
    }
}

public sealed class BackupTransactionException : IOException
{
    public string TransactionId { get; }
    public BackupTransactionException(string message, string transactionId, Exception innerException) : base(message, innerException) => TransactionId = transactionId;
}
