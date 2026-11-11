using System;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace ZonderqOS.SystemCore.Updates
{
    /// <summary>
    /// On-device verifier. Bouncy Castle's managed Ed25519 implementation is
    /// statically referenced and therefore can be compiled by NativeAOT.
    /// Release verification uses an embedded public key, never a USB-supplied key.
    /// </summary>
    public static class ReleaseSignatureVerifier
    {
        public static bool VerifyEd25519(byte[] publicKey, byte[] message, byte[] signature)
        {
            if (publicKey == null || publicKey.Length != 32 ||
                message == null || signature == null || signature.Length != 64)
                return false;

            try
            {
                var verifier = new Ed25519Signer();
                verifier.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
                verifier.BlockUpdate(message, 0, message.Length);
                return verifier.VerifySignature(signature);
            }
            catch
            {
                return false;
            }
        }

        // This method fails closed unless the actual release public key was
        // provisioned at BUILD time. No developer/test key is shipped in builds.
        public static bool HasProductionKey()
        {
            return TryTrustedPublicKey(out _);
        }

        public static bool VerifyProductionManifest(byte[] manifestBytes, byte[] signature)
        {
            return TryTrustedPublicKey(out byte[] publicKey) &&
                VerifyEd25519(publicKey, manifestBytes, signature);
        }

        private static bool TryTrustedPublicKey(out byte[] publicKey)
        {
            publicKey = null;
            string configured = TrustedReleaseKey.PublicKeyHex;
            if (configured == null || configured.Length != 64)
                return false;
            try
            {
                byte[] decoded = Convert.FromHexString(configured);
                if (decoded.Length != 32)
                    return false;
                bool anyNonZero = false;
                for (int i = 0; i < decoded.Length; i++)
                    anyNonZero |= decoded[i] != 0;
                if (!anyNonZero)
                    return false;
                publicKey = decoded;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
