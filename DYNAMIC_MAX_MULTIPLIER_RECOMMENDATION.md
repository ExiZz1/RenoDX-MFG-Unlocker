# DynamicMaxMultiplier — reviewed recommendation, NOT an importable catalog

No new owner-signed catalog/revision is created. Original signed Revision2 is unchanged. Current source discovery shows this setting read-only; RenoDX cannot save it. Use the original addon's Dynamic multiplier ceiling control and restart the game.

Verified official1.8.5 commit:22415f1ba9830719cc7f588d6e04a43e08753897. Full source retrieval verified release tag/commit/tree/archive/Git blob identities.

- File:ReShade.ini. Section:[RenoDX.MFGUnlock]. Key:DynamicMaxMultiplier.
- Semantics: maximum multiplier that NVIDIA Dynamic MFG may choose; not a fixed multiplier and not a command to activate Dynamic MFG.
- Stored integer alternatives:0(Auto),2,3,4,5,6. **1 is invalid**. Default0 leaves the driver/NVIDIA App response unchanged. A future catalog should model this as Enum of exact string tokens, not an integer0..6 range.
- Confirmed introduction/minimum1.8.5. Applies after restart: Streamline reads the response at startup. Saved value is not evidence of the current session's applied ceiling.
- Reader is suppressed in the addon's safe mode and applies only if its custom IsSelectable validator accepts the value. Actual effect also requires NVIDIA's Dynamic MFG support, loaded runtime and the successful in-process NvAPI DRS read hook. No driver profile is written. RenoDX must not manage that hook or force overrides.
- No unconditional INI prerequisite is enforced by the reader for merely storing the preference. DynamicMFG is not a reviewed editable dependency in our current catalog, so do not invent a Requires link to it. Record runtime applicability separately from save validation.

Exact sources:
https://github.com/mavismmg/MFGAdaUnlock-RenoDx/blob/22415f1ba9830719cc7f588d6e04a43e08753897/src/addons/mfgunlock/addon.cpp#L1053
https://github.com/mavismmg/MFGAdaUnlock-RenoDx/blob/22415f1ba9830719cc7f588d6e04a43e08753897/src/addons/mfgunlock/drs_override.hpp#L44
https://github.com/mavismmg/MFGAdaUnlock-RenoDx/blob/22415f1ba9830719cc7f588d6e04a43e08753897/src/addons/mfgunlock/addon_overlay.inl#L3097
https://github.com/mavismmg/MFGAdaUnlock-RenoDx/releases/tag/1.8.5

Why no universal ReShade write parser here: the default lives in a separate atomic initializer, accepted values in a custom function, startup/safe-mode behavior in a conditional reader, UI values in arithmetic mapping, and effect in another hook. A regular expression over get_config_value or its key cannot establish that contract for arbitrary future names. Reliable interprocedural inference would require a substantially broader analyzer. Unrecognized ReShade readers of arbitrary future names remain read-only, tested on synthetic1.8.6/1.9.0.

Safe future fallback: owner-reviewed schema1 catalog revision newer than2, preserving all24 R2 extension entries and15 protected builtins, with a new ReShade.ini Enum entry, exact tokens[0,2,3,4,5,6], default0, minimum1.8.5, restart=true, verified source evidence and reviewed translations. Verify existing catalog-domain RSA-PSS/SHA-256 and monotonic revision before explicit import. No production JSON/signature was prepared or signed for this recommendation; an EXE key-specific exception was not added.
