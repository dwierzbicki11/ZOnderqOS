using System;
using System.Collections.Generic;
using System.IO;

namespace ZonderqOS.SystemCore.Packages
{
    /// <summary>
    /// Stage-1 local package store. Packages are copied into an isolated store;
    /// no payload is activated in /bin, no scripts are executed and no network
    /// access is performed.
    /// </summary>
    public static class PackageManager
    {
        public const string RegistryRoot = "/var/lib/zpkg";
        public const string StoreRoot = "/opt/zpkg";
        public const string ManifestFileName = "package.zpkg";
        public const string PayloadDirectoryName = "payload";

        private const int MaxDepth = 32;
        private const int MaxFiles = 4096;
        private const ulong MaxPackageBytes = 256UL * 1024UL * 1024UL;

        public static bool VerifySource(
            string packageDirectory,
            out PackageManifest manifest,
            out int fileCount,
            out ulong totalBytes,
            out string error)
        {
            manifest = null;
            fileCount = 0;
            totalBytes = 0;
            error = string.Empty;

            string sourceRoot = NormalizePath(packageDirectory);
            if (string.IsNullOrEmpty(sourceRoot) || !Directory.Exists(sourceRoot))
            {
                error = "Package directory does not exist.";
                return false;
            }

            string manifestPath = Combine(sourceRoot, ManifestFileName);
            string payloadPath = Combine(sourceRoot, PayloadDirectoryName);

            if (!File.Exists(manifestPath))
            {
                error = "Missing " + ManifestFileName + ".";
                return false;
            }

            if (!Directory.Exists(payloadPath))
            {
                error = "Missing payload directory.";
                return false;
            }

            try
            {
                string parseError;
                if (!PackageManifest.TryParse(File.ReadAllText(manifestPath), out manifest, out parseError))
                {
                    error = parseError;
                    return false;
                }

                ScanTree(payloadPath, 0, ref fileCount, ref totalBytes);
                return true;
            }
            catch (Exception ex)
            {
                manifest = null;
                fileCount = 0;
                totalBytes = 0;
                error = "Package verification failed: " + ex.Message;
                return false;
            }
        }

        public static bool Install(string packageDirectory, out InstalledPackage installed, out string error)
        {
            installed = null;
            error = string.Empty;

            if (!RequireRoot(out error))
                return false;

            string sourceRoot = NormalizePath(packageDirectory);
            if (IsSameOrChildPath(sourceRoot, StoreRoot))
            {
                error = "Refusing to install a package from the package store itself.";
                return false;
            }

            PackageManifest manifest;
            int verifiedFileCount;
            ulong verifiedBytes;
            if (!VerifySource(sourceRoot, out manifest, out verifiedFileCount, out verifiedBytes, out error))
                return false;

            string payloadPath = Combine(sourceRoot, PayloadDirectoryName);

            if (!EnsureRoots(out error))
                return false;

            string registryPath = GetRegistryPath(manifest.Name);
            string packageRoot = Combine(Combine(StoreRoot, manifest.Name), manifest.Version);
            string stagingRoot = packageRoot + ".installing";

            if (File.Exists(registryPath))
            {
                error = "Package '" + manifest.Name + "' is already installed.";
                return false;
            }

            if (Directory.Exists(packageRoot))
            {
                error = "Package store target already exists: " + packageRoot;
                return false;
            }

            int fileCount = 0;
            ulong totalBytes = 0;
            bool committedPayload = false;

            try
            {
                // A previous hard reset may leave an uncommitted staging directory.
                // It is never considered installed because the registry is written last.
                if (Directory.Exists(stagingRoot))
                {
                    TryDeleteTree(stagingRoot);
                    PermissionManager.RemovePermissionsUnder(stagingRoot);
                    if (Directory.Exists(stagingRoot))
                        throw new IOException("Cannot clean stale package staging directory.");
                }

                Directory.CreateDirectory(stagingRoot);

                CopyTree(payloadPath, stagingRoot, 0, ref fileCount, ref totalBytes);
                if (fileCount != verifiedFileCount || totalBytes != verifiedBytes)
                    throw new InvalidOperationException("Package payload changed while it was being installed.");

                // Commit payload first by renaming the fully copied staging tree.
                // Only after that succeeds is the registry entry published.
                Directory.Move(stagingRoot, packageRoot);
                committedPayload = true;
                PermissionManager.SetPermission(packageRoot, "root", 755);

                File.WriteAllText(registryPath, manifest.Serialize());
                PermissionManager.SetPermission(registryPath, "root", 644);

                installed = new InstalledPackage(
                    manifest.Name,
                    manifest.Version,
                    manifest.Description,
                    packageRoot);

                SecurityLogger.LogEvent(
                    "INFO",
                    "Installed local package " + manifest.Name + " " + manifest.Version +
                    " (" + fileCount + " files, " + totalBytes + " bytes).");
                return true;
            }
            catch (Exception ex)
            {
                error = "Install failed: " + ex.Message;

                // Roll back both the staging tree and a payload that was renamed
                // but whose registry publication failed.
                TryDeleteTree(stagingRoot);
                if (committedPayload)
                    TryDeleteTree(packageRoot);

                try
                {
                    if (File.Exists(registryPath))
                        File.Delete(registryPath);
                }
                catch { }

                PermissionManager.RemovePermissionsUnder(stagingRoot);
                PermissionManager.RemovePermissionsUnder(packageRoot);
                PermissionManager.RemovePermission(registryPath);
                return false;
            }
        }

        public static bool Remove(string packageName, out string error)
        {
            error = string.Empty;

            if (!RequireRoot(out error))
                return false;

            if (!PackageManifest.IsSafeToken(packageName, 64))
            {
                error = "Invalid package name.";
                return false;
            }

            string registryPath = GetRegistryPath(packageName);
            if (!File.Exists(registryPath))
            {
                error = "Package '" + packageName + "' is not installed.";
                return false;
            }

            PackageManifest manifest;
            string parseError;
            try
            {
                if (!PackageManifest.TryParse(File.ReadAllText(registryPath), out manifest, out parseError))
                {
                    error = "Installed package metadata is corrupt: " + parseError;
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = "Cannot read installed package metadata: " + ex.Message;
                return false;
            }

            if (!string.Equals(manifest.Name, packageName, StringComparison.OrdinalIgnoreCase))
            {
                error = "Installed package metadata name does not match its registry entry.";
                return false;
            }

            string packageRoot = Combine(Combine(StoreRoot, manifest.Name), manifest.Version);
            if (!IsSameOrChildPath(packageRoot, StoreRoot) || string.Equals(packageRoot, StoreRoot, StringComparison.Ordinal))
            {
                error = "Package metadata resolved outside the package store.";
                return false;
            }

            try
            {
                if (Directory.Exists(packageRoot))
                    Directory.Delete(packageRoot, true);

                if (Directory.Exists(packageRoot))
                {
                    error = "Package payload directory could not be removed.";
                    return false;
                }

                PermissionManager.RemovePermissionsUnder(packageRoot);
                File.Delete(registryPath);
                PermissionManager.RemovePermission(registryPath);

                string nameRoot = Combine(StoreRoot, manifest.Name);
                try
                {
                    if (Directory.Exists(nameRoot) &&
                        Directory.GetFiles(nameRoot).Length == 0 &&
                        Directory.GetDirectories(nameRoot).Length == 0)
                    {
                        Directory.Delete(nameRoot);
                        PermissionManager.RemovePermission(nameRoot);
                    }
                }
                catch { }

                SecurityLogger.LogEvent("INFO", "Removed local package " + manifest.Name + " " + manifest.Version + ".");
                return true;
            }
            catch (Exception ex)
            {
                error = "Remove failed: " + ex.Message;
                return false;
            }
        }

        public static bool TryGetInstalled(string packageName, out InstalledPackage package, out string error)
        {
            package = null;
            error = string.Empty;

            if (!PackageManifest.IsSafeToken(packageName, 64))
            {
                error = "Invalid package name.";
                return false;
            }

            if (!Directory.Exists(RegistryRoot))
            {
                error = "Package '" + packageName + "' is not installed.";
                return false;
            }

            string registryPath = GetRegistryPath(packageName);
            if (!File.Exists(registryPath))
            {
                error = "Package '" + packageName + "' is not installed.";
                return false;
            }

            try
            {
                PackageManifest manifest;
                string parseError;
                if (!PackageManifest.TryParse(File.ReadAllText(registryPath), out manifest, out parseError))
                {
                    error = "Installed package metadata is corrupt: " + parseError;
                    return false;
                }

                if (!string.Equals(manifest.Name, packageName, StringComparison.OrdinalIgnoreCase))
                {
                    error = "Installed package metadata name does not match its registry entry.";
                    return false;
                }

                package = new InstalledPackage(
                    manifest.Name,
                    manifest.Version,
                    manifest.Description,
                    Combine(Combine(StoreRoot, manifest.Name), manifest.Version));
                return true;
            }
            catch (Exception ex)
            {
                error = "Cannot read installed package metadata: " + ex.Message;
                return false;
            }
        }

        public static bool ListInstalled(List<InstalledPackage> destination, out string error)
        {
            error = string.Empty;
            if (destination == null)
            {
                error = "Destination list is null.";
                return false;
            }

            destination.Clear();
            if (!Directory.Exists(RegistryRoot))
                return true;

            try
            {
                string[] files = Directory.GetFiles(RegistryRoot);
                for (int i = 0; i < files.Length; i++)
                {
                    string file = files[i];
                    if (!file.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
                        continue;

                    PackageManifest manifest;
                    string parseError;
                    if (!PackageManifest.TryParse(File.ReadAllText(file), out manifest, out parseError))
                        continue;

                    string registryName = Path.GetFileNameWithoutExtension(file);
                    if (!string.Equals(registryName, manifest.Name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    destination.Add(new InstalledPackage(
                        manifest.Name,
                        manifest.Version,
                        manifest.Description,
                        Combine(Combine(StoreRoot, manifest.Name), manifest.Version)));
                }

                destination.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                return true;
            }
            catch (Exception ex)
            {
                error = "Cannot enumerate installed packages: " + ex.Message;
                return false;
            }
        }

        private static bool EnsureRoots(out string error)
        {
            error = string.Empty;
            try
            {
                if (!Directory.Exists("/var/lib"))
                    Directory.CreateDirectory("/var/lib");

                if (!Directory.Exists(RegistryRoot))
                {
                    Directory.CreateDirectory(RegistryRoot);
                    PermissionManager.SetPermission(RegistryRoot, "root", 755);
                }

                if (!Directory.Exists(StoreRoot))
                {
                    Directory.CreateDirectory(StoreRoot);
                    PermissionManager.SetPermission(StoreRoot, "root", 755);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "Cannot initialize package database: " + ex.Message;
                return false;
            }
        }

        private static bool RequireRoot(out string error)
        {
            if (!SecurityContext.IsAuthenticated ||
                SecurityContext.CurrentUid != 0 ||
                !string.Equals(SecurityContext.CurrentUser, "root", StringComparison.Ordinal))
            {
                error = "Only authenticated root can modify installed packages.";
                SecurityLogger.LogEvent("WARN", "Unauthorized package-manager modification attempt.");
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static void ScanTree(
            string source,
            int depth,
            ref int fileCount,
            ref ulong totalBytes)
        {
            if (depth > MaxDepth)
                throw new InvalidOperationException("Package directory nesting is too deep.");

            string[] files = Directory.GetFiles(source);
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileName(files[i]);
                ValidatePathSegment(name);

                fileCount++;
                if (fileCount > MaxFiles)
                    throw new InvalidOperationException("Package contains too many files.");

                long length = new FileInfo(files[i]).Length;
                if (length < 0)
                    throw new InvalidOperationException("Package contains an invalid file length.");

                ulong unsignedLength = (ulong)length;
                if (unsignedLength > MaxPackageBytes || totalBytes > MaxPackageBytes - unsignedLength)
                    throw new InvalidOperationException("Package exceeds maximum installed size.");

                totalBytes += unsignedLength;
            }

            string[] directories = Directory.GetDirectories(source);
            for (int i = 0; i < directories.Length; i++)
            {
                string name = Path.GetFileName(directories[i]);
                ValidatePathSegment(name);
                ScanTree(directories[i], depth + 1, ref fileCount, ref totalBytes);
            }
        }

        private static void CopyTree(
            string source,
            string destination,
            int depth,
            ref int fileCount,
            ref ulong totalBytes)
        {
            if (depth > MaxDepth)
                throw new InvalidOperationException("Package directory nesting is too deep.");

            if (!Directory.Exists(destination))
                Directory.CreateDirectory(destination);

            string[] files = Directory.GetFiles(source);
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileName(files[i]);
                ValidatePathSegment(name);

                fileCount++;
                if (fileCount > MaxFiles)
                    throw new InvalidOperationException("Package contains too many files.");

                long length = new FileInfo(files[i]).Length;
                if (length < 0)
                    throw new InvalidOperationException("Package contains an invalid file length.");

                ulong unsignedLength = (ulong)length;
                if (unsignedLength > MaxPackageBytes || totalBytes > MaxPackageBytes - unsignedLength)
                    throw new InvalidOperationException("Package exceeds maximum installed size.");

                string target = Combine(destination, name);
                if (File.Exists(target) || Directory.Exists(target))
                    throw new InvalidOperationException("Duplicate payload target: " + name);

                // Recheck the limit while reading: the source can grow after
                // FileInfo.Length was sampled above.
                byte[] buffer = new byte[16 * 1024];
                using (FileStream input = File.OpenRead(files[i]))
                using (FileStream output = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                {
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if ((ulong)read > MaxPackageBytes - totalBytes)
                            throw new InvalidOperationException("Package exceeds maximum installed size.");

                        output.Write(buffer, 0, read);
                        totalBytes += (ulong)read;
                    }
                }
            }

            string[] directories = Directory.GetDirectories(source);
            for (int i = 0; i < directories.Length; i++)
            {
                string name = Path.GetFileName(directories[i]);
                ValidatePathSegment(name);
                string target = Combine(destination, name);

                if (File.Exists(target))
                    throw new InvalidOperationException("Payload directory collides with a file: " + name);

                CopyTree(directories[i], target, depth + 1, ref fileCount, ref totalBytes);
            }
        }

        private static void ValidatePathSegment(string name)
        {
            if (string.IsNullOrEmpty(name) ||
                name == "." ||
                name == ".." ||
                name.IndexOf('/') >= 0 ||
                name.IndexOf('\\') >= 0 ||
                name.IndexOf(':') >= 0)
            {
                throw new InvalidOperationException("Unsafe package path segment.");
            }
        }

        private static void TryDeleteTree(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch { }
        }

        private static string GetRegistryPath(string packageName)
        {
            return Combine(RegistryRoot, packageName + ".pkg");
        }

        private static string Combine(string left, string right)
        {
            string a = NormalizePath(left);
            string b = (right ?? string.Empty).Replace('\\', '/').Trim('/');
            if (a == "/")
                return "/" + b;
            return a + "/" + b;
        }

        private static string NormalizePath(string path)
        {
            string value = (path ?? string.Empty).Replace('\\', '/');
            while (value.Contains("//", StringComparison.Ordinal))
                value = value.Replace("//", "/", StringComparison.Ordinal);
            while (value.Length > 1 && value.EndsWith("/", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 1);
            return value;
        }

        private static bool IsSameOrChildPath(string path, string root)
        {
            string value = NormalizePath(path);
            string parent = NormalizePath(root);
            return string.Equals(value, parent, StringComparison.Ordinal) ||
                   value.StartsWith(parent + "/", StringComparison.Ordinal);
        }
    }
}
