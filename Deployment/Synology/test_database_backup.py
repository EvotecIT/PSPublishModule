"""Hermetic receiver contracts; run on Linux with python3 -B -m unittest."""
import datetime as dt
import fcntl
import hashlib
import importlib.util
import io
import json
import pathlib
import os
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('receiver', pathlib.Path(__file__).with_name('powerforge-database-backup.py'))
R = importlib.util.module_from_spec(spec)
spec.loader.exec_module(R)


class ReceiverContracts(unittest.TestCase):
    def setUp(self):
        self.original_umask = os.umask(0o077)
        self.temp = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.temp.name).resolve()
        self.root.chmod(0o700)
        (self.root / '.powerforge-database-backup-root').write_text(R.MARKER)
        (self.root / 'snapshots').mkdir()
        (self.root / 'incoming').mkdir()
        (self.root / '.receiver.lock').touch()
        (self.root / '.catalog.lock').touch()
        self.config = {'root': str(self.root), 'hostId': 'sample', 'keepDaily': 14, 'maxAgeHours': 30,
                       'maxArchiveBytes': 1024 * 1024, 'minimumFreeBytes': 0, 'databases': {'sample': 'sqlite'}}
        self.receiver = R.Receiver(self.config)

    def tearDown(self):
        try:
            self.temp.cleanup()
        finally:
            os.umask(self.original_umask)

    def upload(self, days=0, suffix='01234567', mutate=None, dotnet_timestamp=False, backfill=False, mutate_manifest=None):
        now = (R.utc_now() - dt.timedelta(days=days, seconds=1)).replace(microsecond=0)
        created = now.strftime('%Y-%m-%dT%H:%M:%S') + '.1234567+00:00' if dotnet_timestamp else now.isoformat()
        stamp = now.strftime('%Y%m%dT%H%M%SZ') + '-' + suffix
        database = b'SQLite format 3\x00synthetic-fixture'
        databases = [{'id': 'sample', 'provider': 'sqlite', 'entry': 'sample.sqlite', 'capturedAtUtc': created,
                      'verified': True, 'sizeBytes': len(database), 'sha256': hashlib.sha256(database).hexdigest()}]
        recovery = [('config/' + identity + '.json', b'{"service":"synthetic-fixture"}') for identity in self.config.get('configurationIds', [])]
        recovery_files = [{'entry': name, 'sizeBytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()} for name, data in recovery]
        manifest_data = {'schemaVersion': 2, 'format': 'native', 'hostId': 'sample', 'stamp': stamp, 'createdAtUtc': created, 'databases': databases, 'files': recovery_files}
        if mutate_manifest:
            mutate_manifest(manifest_data)
        manifest = json.dumps(manifest_data).encode()
        entries = [('sample.sqlite', database)] + recovery + [('manifest.json', manifest)]
        files = [{'entry': name, 'sizeBytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()} for name, data in entries]
        body = b''.join(data for _, data in entries)
        header = {'schemaVersion': 2, 'format': 'native', 'hostId': 'sample', 'stamp': stamp,
                  'createdAtUtc': created, 'sizeBytes': len(body), 'databases': databases, 'files': files}
        if mutate:
            mutate(header)
        stream = io.BytesIO(json.dumps(header).encode() + b'\n' + body)
        if backfill:
            return self.receiver.receive(stream)
        # Historical fixtures were uploaded on their capture day, not backfilled today.
        with patch.object(R, 'utc_now', return_value=now + dt.timedelta(seconds=1)):
            return self.receiver.receive(stream)

    def test_upload_cannot_backfill_history_outside_the_freshness_window(self):
        trusted = self.upload()
        with self.assertRaisesRegex(ValueError, 'freshness window'):
            self.upload(days=14, backfill=True)
        self.assertEqual([trusted['stamp']], [p.name for p in self.receiver.snapshots.iterdir()])

    def test_empty_or_nonportable_database_and_recovery_inventories_are_rejected(self):
        for databases in ({}, None, {'sample': 'unknown'}, {'sample\n': 'sqlite'}, {'con': 'sqlite'}):
            with self.subTest(databases=databases), self.assertRaisesRegex(ValueError, 'inventory'):
                R.Receiver(dict(self.config, databases=databases))
        for configurations in (None, ['service', 'service'], ['service\n'], ['nul']):
            with self.subTest(configurations=configurations), self.assertRaisesRegex(ValueError, 'inventory'):
                R.Receiver(dict(self.config, configurationIds=configurations))

    def test_cross_filesystem_publication_is_rejected_before_receive(self):
        stat = pathlib.Path.stat
        def changed(path, *args, **kwargs):
            metadata = stat(path, *args, **kwargs)
            if path == self.root / 'incoming':
                values = list(metadata); values[2] = metadata.st_dev + 1
                return os.stat_result(values)
            return metadata
        with patch.object(pathlib.Path, 'stat', changed):
            with self.assertRaisesRegex(ValueError, 'share a filesystem'):
                R.Receiver(self.config)
        self.assertEqual([], list(self.receiver.incoming.iterdir()))

    def test_capacity_check_uses_the_filesystem_receiving_the_bytes(self):
        disk_usage = R.shutil.disk_usage
        with patch.object(R.shutil, 'disk_usage', wraps=disk_usage) as capacity:
            self.upload()
        capacity.assert_called_once_with(self.receiver.incoming)

    def test_invalid_capacity_policy_is_rejected_before_upload(self):
        for field, values in (('minimumFreeBytes', (-1, True, 1.5, None)), ('maxArchiveBytes', (0, -1, True, 1.5, None))):
            for value in values:
                with self.subTest(field=field, value=value), self.assertRaisesRegex(ValueError, 'free-space reserve'):
                    R.Receiver(dict(self.config, **{field: value}))

    def test_receiver_rejects_storage_owned_by_another_identity(self):
        with patch.object(R.os, 'geteuid', return_value=os.geteuid() + 1):
            with self.assertRaisesRegex(ValueError, 'owned by the receiver identity'):
                R.Receiver(self.config)

    def test_managed_directories_and_locks_require_trusted_ownership(self):
        stat = pathlib.Path.stat
        for target in (self.root / 'snapshots', self.root / 'incoming', self.root / '.receiver.lock', self.root / '.catalog.lock'):
            def changed(path, *args, **kwargs):
                metadata = stat(path, *args, **kwargs)
                if path == target:
                    values = list(metadata); values[4] = metadata.st_uid + 1
                    return os.stat_result(values)
                return metadata
            with self.subTest(path=target.name), patch.object(pathlib.Path, 'stat', changed):
                with self.assertRaisesRegex(ValueError, 'owned by the receiver identity'):
                    R.Receiver(self.config)

    def test_recovery_inventory_mismatch_is_rejected_before_publication(self):
        self.config['configurationIds'] = ['service']
        for mutation in (lambda manifest: manifest.update(files=[]), lambda manifest: manifest['files'][0].update(sha256='0' * 64)):
            with self.assertRaisesRegex(ValueError, 'recovery manifest differs'):
                self.upload(mutate_manifest=mutation)
            self.assertEqual([], list(self.receiver.snapshots.iterdir()))
            self.assertEqual([], list(self.receiver.incoming.iterdir()))

    def test_oversized_recovery_metadata_is_rejected_before_it_can_be_parsed(self):
        self.config['maxArchiveBytes'] = 3 * 1024 * 1024
        with self.assertRaisesRegex(ValueError, 'metadata size'):
            self.upload(mutate_manifest=lambda manifest: manifest.update(padding='x' * (1024 * 1024)))
        self.assertEqual([], list(self.receiver.snapshots.iterdir()))
        self.assertEqual([], list(self.receiver.incoming.iterdir()))
        trusted = self.upload()
        manifest = self.receiver.snapshots / trusted['stamp'] / 'manifest.json'
        manifest.write_bytes(manifest.read_bytes() + b' ' * (1024 * 1024))
        loads = R.json.loads
        def bounded_loads(text, *args, **kwargs):
            self.assertLessEqual(len(text), 1024 * 1024, 'Oversized metadata reached the JSON parser')
            return loads(text, *args, **kwargs)
        with patch.object(R.json, 'loads', side_effect=bounded_loads):
            with self.assertRaisesRegex(ValueError, 'metadata size'):
                self.receiver.status()

    def test_configuration_inventory_remains_bound_during_status_and_maintenance(self):
        self.config['configurationIds'] = ['service']
        receipt = self.upload()
        self.assertTrue(self.receiver.status(verify=True)['healthy'])
        self.assertEqual(receipt['stamp'], self.receiver.maintain()['verifiedStamp'])

    def test_same_day_upload_cannot_replace_a_previously_retained_daily_point(self):
        trusted = self.upload()
        self.receiver.maintain()
        self.upload(suffix='89abcdef')
        result = self.receiver.maintain()
        self.assertEqual(trusted['stamp'], result['dailyKeepers'][trusted['stamp'][:8]])
        self.assertTrue((self.receiver.snapshots / trusted['stamp']).exists())
        self.assertTrue(self.receiver.status()['healthy'])

    def test_failed_maintenance_does_not_forget_adopted_daily_points(self):
        trusted = self.upload()
        before = self.receiver.maintain()['dailyKeepers']
        archive = self.receiver.snapshots / trusted['stamp'] / 'sample.sqlite'
        archive.write_bytes(b'X' * archive.stat().st_size)
        with self.assertRaises(ValueError):
            self.receiver.maintain()
        self.assertEqual(before, json.loads((self.root / 'maintenance.json').read_text())['dailyKeepers'])

    def test_daily_selection_is_directory_flushed_before_maintenance_succeeds(self):
        trusted = self.upload()
        committed = []
        flush = R.sync_directory
        def observe(directory):
            if directory == self.root:
                committed.append(json.loads((self.root / 'maintenance.json').read_text())['dailyKeepers'])
            flush(directory)
        with patch.object(R, 'sync_directory', side_effect=observe):
            result = self.receiver.maintain()
        self.assertEqual([result['dailyKeepers']], committed)
        self.assertEqual(trusted['stamp'], committed[0][trusted['stamp'][:8]])

    def test_complete_upload_is_published_with_bound_receipt_and_reverified(self):
        receipt = self.upload()
        status = self.receiver.status(verify=True)
        self.assertTrue(status['healthy'])
        self.assertTrue(status['latest']['checksumPass'])
        self.assertEqual(receipt['captureSha256'], status['latest']['captureSha256'])
        self.assertEqual([], list(self.receiver.incoming.iterdir()))

    def test_stalled_header_and_body_release_the_writer_lock_at_the_nas_deadline(self):
        trusted = self.upload()
        self.config['receiveTimeoutSeconds'] = 1
        receiver = R.Receiver(self.config)
        header = json.loads((receiver.snapshots / trusted['stamp'] / 'capture.json').read_text())
        header['stamp'] = header['stamp'][:-8] + '89abcdef'
        for payload in (b'', json.dumps(header).encode() + b'\n'):
            with self.subTest(header_sent=bool(payload)):
                read_fd, write_fd = os.pipe()
                try:
                    with os.fdopen(read_fd, 'rb') as reader:
                        if payload:
                            os.write(write_fd, payload)
                        with self.assertRaisesRegex(TimeoutError, 'time budget exceeded'):
                            R.dispatch(receiver, 'receive', reader)
                finally:
                    os.close(write_fd)
                self.assertEqual([], list(receiver.incoming.iterdir()))
                self.assertEqual(trusted['stamp'], R.dispatch(receiver, 'maintain')['verifiedStamp'])

    def test_post_rename_sync_failure_preserves_published_copy_and_original_error(self):
        sync = R.sync_directory
        def fail_incoming(directory):
            if directory == self.receiver.incoming:
                raise OSError('synthetic publication synchronization failure')
            sync(directory)
        with patch.object(R, 'sync_directory', side_effect=fail_incoming):
            with self.assertRaisesRegex(OSError, 'synthetic publication synchronization failure'):
                self.upload()
        self.assertEqual([], list(self.receiver.incoming.iterdir()))
        self.assertEqual(1, len(list(self.receiver.snapshots.iterdir())))
        self.assertTrue(self.receiver.status(verify=True)['healthy'])

    def test_dotnet_fractional_timestamps_pass_upload_status_and_maintenance(self):
        receipt = self.upload(dotnet_timestamp=True)
        status = self.receiver.status(verify=True)
        self.assertEqual(receipt['stamp'], status['latest']['stamp'])
        self.assertEqual(123456, R.timestamp(status['latest']['createdAtUtc']).microsecond)
        self.assertTrue(self.receiver.maintain()['healthy'])
        for fraction in ('1', '12', '123', '1234', '12345', '123456', '1234567'):
            self.assertEqual(int(fraction.ljust(6, '0')[:6]),
                             R.timestamp('2026-10-13T08:18:15.' + fraction + '+00:00').microsecond)

    def test_corrupt_upload_never_becomes_a_completed_recovery_point(self):
        with self.assertRaises(ValueError):
            self.upload(mutate=lambda h: h['files'][1].update(sha256='0' * 64))
        self.assertEqual([], list(self.receiver.snapshots.iterdir()))
        self.assertEqual([], list(self.receiver.incoming.iterdir()))

    def test_required_database_omission_is_rejected(self):
        with self.assertRaises(ValueError):
            self.upload(mutate=lambda h: h.update(databases=[]))

    def test_unverified_database_is_rejected(self):
        with self.assertRaises(ValueError):
            self.upload(mutate=lambda h: h['databases'][0].update(verified=False))

    def test_retention_keeps_fourteen_distinct_successful_days(self):
        for day in range(16):
            self.upload(days=day)
        self.upload(suffix='89abcdef')
        result = self.receiver.maintain()
        self.assertEqual(14, result['retainedDaily'])
        self.assertEqual(14, len(list(self.receiver.snapshots.iterdir())))

    def test_stale_latest_holds_retention_and_records_failure(self):
        self.upload(days=2)
        with self.assertRaises(ValueError):
            self.receiver.maintain()
        self.assertEqual(1, len(list(self.receiver.snapshots.iterdir())))
        self.assertFalse(json.loads((self.root / 'maintenance.json').read_text())['healthy'])

    def test_changed_stored_archive_is_detected(self):
        receipt = self.upload()
        archive = self.receiver.snapshots / receipt['stamp'] / 'sample.sqlite'
        archive.write_bytes(b'X' * archive.stat().st_size)
        with self.assertRaises(ValueError):
            self.receiver.status(verify=True)

    def test_symlink_cannot_redirect_status_or_retention(self):
        receipt = self.upload()
        path = self.receiver.snapshots / receipt['stamp'] / 'capture.json'
        path.unlink()
        path.symlink_to(self.root / '.powerforge-database-backup-root')
        with self.assertRaises(ValueError):
            self.receiver.status()

    def test_marked_abandoned_upload_is_removed_after_a_day(self):
        self.upload()
        folder = self.receiver.incoming / ('20261001T000000Z-01234567-' + 'a' * 32)
        folder.mkdir()
        (folder / '.powerforge-incoming').write_text(R.MARKER)
        (folder / 'partial.sqlite').write_bytes(b'partial')
        old = R.utc_now().timestamp() - 90000
        os.utime(folder, (old, old))
        result = self.receiver.maintain()
        self.assertEqual(1, result['removedPartial'])
        self.assertFalse(folder.exists())

    def test_status_reads_previous_completed_set_while_writer_lock_is_held(self):
        receipt = self.upload()
        with (self.root / '.receiver.lock').open('r+') as writer:
            fcntl.flock(writer, fcntl.LOCK_EX | fcntl.LOCK_NB)
            result = R.dispatch(self.receiver, 'status')
        self.assertTrue(result['healthy'])
        self.assertEqual(receipt['stamp'], result['latest']['stamp'])

    def test_corrupted_older_day_keeper_preserves_its_intact_alternative(self):
        self.upload()
        intact = self.upload(days=1, suffix='01234567')
        corrupted = self.upload(days=1, suffix='89abcdef')
        path = self.receiver.snapshots / corrupted['stamp'] / 'sample.sqlite'
        path.write_bytes(b'X' * path.stat().st_size)
        with self.assertRaises(ValueError):
            self.receiver.maintain()
        self.assertTrue((self.receiver.snapshots / intact['stamp']).exists())
        self.assertFalse(json.loads((self.root / 'maintenance.json').read_text())['healthy'])

    def test_corruption_in_an_older_unique_daily_point_is_detected(self):
        self.upload()
        older = self.upload(days=1)
        self.receiver.maintain()
        path = self.receiver.snapshots / older['stamp'] / 'sample.sqlite'
        path.write_bytes(b'X' * path.stat().st_size)
        with self.assertRaisesRegex(ValueError, 'hash mismatch'):
            self.receiver.maintain()
        self.assertTrue((self.receiver.snapshots / older['stamp']).exists())
        self.assertFalse(json.loads((self.root / 'maintenance.json').read_text())['healthy'])


if __name__ == '__main__':
    unittest.main()
