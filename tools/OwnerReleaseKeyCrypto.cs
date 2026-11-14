using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;

namespace RenoDX.Publisher
{
    public sealed class EncryptedReleaseKey
    {
        public string EncryptedPrivatePem {get;set;}="";
        public string PublicSpki {get;set;}="";
        public string Fingerprint {get;set;}="";
    }
    // Publisher workstation only. This file is neither compiled nor embedded in the app.
    public static class OwnerReleaseKeyCrypto
    {
        private static char[] Password(SecureString password)
        {
            var pointer=Marshal.SecureStringToBSTR(password);
            try {var chars=new char[password.Length];Marshal.Copy(pointer,chars,0,chars.Length);return chars;}
            finally {Marshal.ZeroFreeBSTR(pointer);}
        }
        public static EncryptedReleaseKey Create(SecureString password)
        {
            if(password.Length<20)throw new ArgumentException("Use a strong passphrase of at least 20 characters.");
            var chars=Password(password);
            try {
                using(var rsa=RSA.Create(4096)) {
                    var publicBytes=rsa.ExportSubjectPublicKeyInfo();
                    return new EncryptedReleaseKey {
                        EncryptedPrivatePem=rsa.ExportEncryptedPkcs8PrivateKeyPem(chars,new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc,HashAlgorithmName.SHA256,600000)),
                        PublicSpki=Convert.ToBase64String(publicBytes),
                        Fingerprint=Convert.ToHexString(SHA256.HashData(publicBytes))
                    };
                }
            }finally {Array.Clear(chars,0,chars.Length);}
        }
        public static RSA Unlock(string encryptedPem,SecureString password)
        {
            var chars=Password(password);var rsa=RSA.Create();
            try {rsa.ImportFromEncryptedPem(encryptedPem,chars);return rsa;}
            catch {rsa.Dispose();throw;}
            finally {Array.Clear(chars,0,chars.Length);}
        }
    }
}
