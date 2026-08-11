namespace PowerForge.Tests;

public sealed class AppleArchiveBuildSnapshotTests
{
    [Fact]
    public void CopyDirectory_restores_read_only_directory_modes_after_copying_descendants()
    {
#if NET8_0_OR_GREATER
        if (OperatingSystem.IsWindows())
            return;

        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var source = Directory.CreateDirectory(Path.Combine(root.FullName, "source"));
        var readOnly = Directory.CreateDirectory(Path.Combine(source.FullName, "Contents"));
        File.WriteAllText(Path.Combine(readOnly.FullName, "payload"), "approved artifact");
        File.SetUnixFileMode(
            readOnly.FullName,
            UnixFileMode.UserRead | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        var destination = Path.Combine(root.FullName, "destination");
        try
        {
            AppleArtifactCopy.CopyDirectory(source.FullName, destination);

            Assert.Equal("approved artifact", File.ReadAllText(Path.Combine(destination, "Contents", "payload")));
            Assert.Equal(File.GetUnixFileMode(readOnly.FullName), File.GetUnixFileMode(Path.Combine(destination, "Contents")));
        }
        finally
        {
            try
            {
                File.SetUnixFileMode(readOnly.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var copiedReadOnly = Path.Combine(destination, "Contents");
                if (Directory.Exists(copiedReadOnly))
                    File.SetUnixFileMode(copiedReadOnly, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                root.Delete(recursive: true);
            }
            catch { }
        }
#endif
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoveExistingPathToBackupIfUnchanged_preserves_concurrent_replacement(bool directory)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var destination = Path.Combine(root.FullName, directory ? "App.xcarchive" : "App.zip");
            if (directory)
            {
                Directory.CreateDirectory(destination);
                File.WriteAllText(Path.Combine(destination, "payload"), "observed artifact");
            }
            else
            {
                File.WriteAllText(destination, "observed artifact");
            }
            var observed = AppleArtifactCopy.CaptureRegularPathIdentity(destination, "Apple artifact");
            if (directory)
            {
                Directory.Delete(destination, recursive: true);
                Directory.CreateDirectory(destination);
                File.WriteAllText(Path.Combine(destination, "payload"), "concurrent replacement");
            }
            else
            {
                File.WriteAllText(destination, "concurrent replacement");
            }
            var backup = Path.Combine(root.FullName, ".backup", Path.GetFileName(destination));

            var exception = Assert.Throws<InvalidOperationException>(() =>
                AppleArtifactCopy.MoveExistingPathToBackupIfUnchanged(
                    destination,
                    backup,
                    observed,
                    "Apple artifact"));

            Assert.Contains("changed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(backup));
            Assert.False(File.Exists(backup));
            var payload = directory
                ? File.ReadAllText(Path.Combine(destination, "payload"))
                : File.ReadAllText(destination);
            Assert.Equal("concurrent replacement", payload);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void RemoveBackupIfUnchanged_preserves_concurrent_backup_replacement()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var backup = Directory.CreateDirectory(Path.Combine(root.FullName, ".backup"));
            File.WriteAllText(Path.Combine(backup.FullName, "payload"), "observed artifact");
            var observed = AppleArtifactCopy.CaptureRegularPathIdentity(backup.FullName, "Apple artifact")!;
            File.WriteAllText(Path.Combine(backup.FullName, "payload"), "concurrent replacement");
            var quarantine = Path.Combine(root.FullName, ".quarantine");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                AppleArtifactCopy.RemoveBackupIfUnchanged(
                    backup.FullName,
                    quarantine,
                    observed,
                    "Apple artifact"));

            Assert.Contains("retained", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("concurrent replacement", File.ReadAllText(Path.Combine(backup.FullName, "payload")));
            Assert.False(Directory.Exists(quarantine));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void RemoveBackupIfUnchanged_deletes_verified_backup_with_read_only_nested_directory()
    {
#if NET8_0_OR_GREATER
        if (OperatingSystem.IsWindows())
            return;

        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var backupParent = Directory.CreateDirectory(Path.Combine(root.FullName, ".App.powerforge-backup-test"));
        var backup = Directory.CreateDirectory(Path.Combine(backupParent.FullName, "App.xcarchive"));
        var readOnly = Directory.CreateDirectory(Path.Combine(backup.FullName, "Products"));
        File.WriteAllText(Path.Combine(readOnly.FullName, "payload"), "previous archive");
        File.SetUnixFileMode(
            readOnly.FullName,
            UnixFileMode.UserRead | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        var observed = AppleArtifactCopy.CaptureRegularPathIdentity(backup.FullName, "Apple archive")!;
        var quarantine = Path.Combine(root.FullName, ".App.powerforge-rollback-test");
        try
        {
            AppleArtifactCopy.RemoveBackupIfUnchanged(
                backup.FullName,
                quarantine,
                observed,
                "Apple archive");

            Assert.False(Directory.Exists(backupParent.FullName));
            Assert.False(Directory.Exists(quarantine));
        }
        finally
        {
            try
            {
                if (Directory.Exists(readOnly.FullName))
                    File.SetUnixFileMode(readOnly.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                root.Delete(recursive: true);
            }
            catch { }
        }
#endif
    }

    [Fact]
    public void RemoveBackupIfUnchanged_does_not_fail_after_verified_backup_deletion_when_parent_cleanup_is_denied()
    {
#if NET8_0_OR_GREATER
        if (OperatingSystem.IsWindows())
            return;

        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var backupParent = Directory.CreateDirectory(Path.Combine(root.FullName, ".App.powerforge-backup-test"));
        var backup = Directory.CreateDirectory(Path.Combine(backupParent.FullName, "App.xcarchive"));
        File.WriteAllText(Path.Combine(backup.FullName, "payload"), "previous archive");
        var observed = AppleArtifactCopy.CaptureRegularPathIdentity(backup.FullName, "Apple archive")!;
        var quarantineRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var quarantine = Path.Combine(quarantineRoot.FullName, ".App.powerforge-rollback-test");
        try
        {
            File.SetUnixFileMode(
                root.FullName,
                UnixFileMode.UserRead | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            AppleArtifactCopy.RemoveBackupIfUnchanged(
                backup.FullName,
                quarantine,
                observed,
                "Apple archive");

            Assert.False(Directory.Exists(backup.FullName));
            Assert.True(Directory.Exists(backupParent.FullName));
            Assert.False(Directory.Exists(quarantine));
        }
        finally
        {
            try
            {
                File.SetUnixFileMode(root.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                root.Delete(recursive: true);
                quarantineRoot.Delete(recursive: true);
            }
            catch { }
        }
#endif
    }

    [Fact]
    public void DirectExport_publish_rejects_artifact_replaced_after_xcodebuild_identity_was_observed()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var destination = Path.Combine(root.FullName, "export");
            using var snapshot = AppleDirectExportSnapshot.Create();
            var artifact = Directory.CreateDirectory(Path.Combine(snapshot.ExportPath, "App.app"));
            var payload = Path.Combine(artifact.FullName, "payload");
            File.WriteAllText(payload, "approved export");
            var expected = AppleNotarizationService.ComputeArtifactSha256(artifact.FullName);
            snapshot.BindProducedArtifact(artifact.FullName, expected);
            File.WriteAllText(payload, "replacement export");

            var exception = Assert.Throws<InvalidOperationException>(() => snapshot.Publish(destination));

            Assert.Contains("changed after xcodebuild completed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(destination));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Publish_rejects_archive_replaced_after_xcodebuild_identity_was_observed()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var destination = Path.Combine(root.FullName, "App.xcarchive");
            using var snapshot = AppleArchiveBuildSnapshot.Create(destination);
            var archive = Directory.CreateDirectory(snapshot.ArchivePath);
            var payload = Path.Combine(archive.FullName, "payload");
            File.WriteAllText(payload, "approved archive");
            var expected = AppleNotarizationService.ComputeArtifactSha256(snapshot.ArchivePath);
            File.WriteAllText(payload, "replacement archive");

            var exception = Assert.Throws<InvalidOperationException>(() => snapshot.Publish(destination, expected));

            Assert.Contains("changed after xcodebuild completed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(destination));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void RestoreDirectoryBackup_retains_previous_artifact_when_destination_was_recreated()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcarchive"));
            File.WriteAllText(Path.Combine(destination.FullName, "payload"), "concurrent artifact");
            var backup = Directory.CreateDirectory(Path.Combine(root.FullName, ".App.xcarchive.powerforge-backup-test"));
            File.WriteAllText(Path.Combine(backup.FullName, "payload"), "previous artifact");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                AppleArtifactCopy.RestoreDirectoryBackup(destination.FullName, backup.FullName));

            Assert.Contains("retained", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("concurrent artifact", File.ReadAllText(Path.Combine(destination.FullName, "payload")));
            Assert.Equal("previous artifact", File.ReadAllText(Path.Combine(backup.FullName, "payload")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void RollbackPublication_preserves_concurrently_replaced_archive_and_previous_backup()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var approved = Directory.CreateDirectory(Path.Combine(root.FullName, "approved"));
            File.WriteAllText(Path.Combine(approved.FullName, "payload"), "published archive");
            var publishedSha256 = AppleNotarizationService.ComputeArtifactSha256(approved.FullName);
            approved.Delete(recursive: true);
            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcarchive"));
            File.WriteAllText(Path.Combine(destination.FullName, "payload"), "concurrent archive");
            var backup = Directory.CreateDirectory(Path.Combine(root.FullName, ".App.xcarchive.powerforge-backup-test"));
            File.WriteAllText(Path.Combine(backup.FullName, "payload"), "previous archive");
            var rollbackCandidate = Path.Combine(root.FullName, ".App.xcarchive.powerforge-failed-test");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                AppleArchiveBuildSnapshot.RollbackPublication(
                    destination.FullName,
                    backup.FullName,
                    rollbackCandidate,
                    publishedSha256,
                    published: true,
                    movedExisting: true));

            Assert.Contains("no unrecognized archive bytes were deleted", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("concurrent archive", File.ReadAllText(Path.Combine(destination.FullName, "payload")));
            Assert.Equal("previous archive", File.ReadAllText(Path.Combine(backup.FullName, "payload")));
            Assert.False(Directory.Exists(rollbackCandidate));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void DirectExportRollback_preserves_concurrently_replaced_export_and_previous_backup()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var approved = Directory.CreateDirectory(Path.Combine(root.FullName, "approved-export"));
            File.WriteAllText(Path.Combine(approved.FullName, "payload"), "published export");
            var publishedSha256 = AppleNotarizationService.ComputeArtifactSha256(approved.FullName);
            approved.Delete(recursive: true);
            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, "export"));
            File.WriteAllText(Path.Combine(destination.FullName, "payload"), "concurrent export");
            var backup = Directory.CreateDirectory(Path.Combine(root.FullName, ".export.powerforge-backup-test"));
            File.WriteAllText(Path.Combine(backup.FullName, "payload"), "previous export");
            var rollbackCandidate = Path.Combine(root.FullName, ".export.powerforge-failed-test");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                AppleDirectExportSnapshot.RollbackPublication(
                    destination.FullName,
                    backup.FullName,
                    rollbackCandidate,
                    publishedSha256,
                    published: true,
                    movedExisting: true));

            Assert.Contains("no unrecognized export bytes were deleted", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("concurrent export", File.ReadAllText(Path.Combine(destination.FullName, "payload")));
            Assert.Equal("previous export", File.ReadAllText(Path.Combine(backup.FullName, "payload")));
            Assert.False(Directory.Exists(rollbackCandidate));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void DirectExportRollback_preserves_concurrently_replaced_directory_link_without_traversing_it()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var approved = Directory.CreateDirectory(Path.Combine(root.FullName, "approved-export"));
            File.WriteAllText(Path.Combine(approved.FullName, "payload"), "published export");
            var publishedSha256 = AppleNotarizationService.ComputeArtifactSha256(approved.FullName);
            approved.Delete(recursive: true);
            var concurrentTarget = Directory.CreateDirectory(Path.Combine(root.FullName, "concurrent-export"));
            File.WriteAllText(Path.Combine(concurrentTarget.FullName, "payload"), "concurrent export");
            var destination = Path.Combine(root.FullName, "export");
            Directory.CreateSymbolicLink(destination, concurrentTarget.FullName);
            var backup = Directory.CreateDirectory(Path.Combine(root.FullName, ".export.powerforge-backup-test"));
            File.WriteAllText(Path.Combine(backup.FullName, "payload"), "previous export");
            var rollbackCandidate = Path.Combine(root.FullName, ".export.powerforge-failed-test");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                AppleDirectExportSnapshot.RollbackPublication(
                    destination,
                    backup.FullName,
                    rollbackCandidate,
                    publishedSha256,
                    published: true,
                    movedExisting: true));

            Assert.Contains("no unrecognized export bytes were deleted", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(concurrentTarget.FullName, new DirectoryInfo(destination).LinkTarget);
            Assert.Equal("concurrent export", File.ReadAllText(Path.Combine(concurrentTarget.FullName, "payload")));
            Assert.Equal("previous export", File.ReadAllText(Path.Combine(backup.FullName, "payload")));
            Assert.False(Directory.Exists(rollbackCandidate));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
