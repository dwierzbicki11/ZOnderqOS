namespace ZonderqOS.SystemCore.Updates
{
    /// <summary>
    /// Release public key provisioned by the build. Empty in normal developer
    /// builds: update installation remains disabled until a real publisher
    /// key is explicitly embedded. Only this PUBLIC key can be distributed.
    /// </summary>
    internal static partial class TrustedReleaseKey
    {
        private static string GetProvisionedHex()
        {
            string result = string.Empty;
            SupplyPublicKey(ref result);
            return result;
        }

        internal static string PublicKeyHex => GetProvisionedHex();

        // A generated partial method may provide an immutable public key.
        // No implementation means empty key and a fail-closed updater.
        static partial void SupplyPublicKey(ref string publicKeyHex);
    }
}
