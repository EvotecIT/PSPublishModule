"""Hermetic receiver contracts; run on Linux with python3 -B -m unittest."""
import datetime as dt
import hashlib
import importlib.util
import io
import json
import pathlib
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('receiver', pathlib.Path(__file__).with_name('powerforge-database-backup.py'))
R = importlib.util.module_from_spec(spec)
spec.loader.exec_module(R)


class ReceiverContracts(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.temp.name).resolve()
        self.root.chmod(0o700)
        (self.root / '.powerforge-database-backup-root').write_text(R.MARKER)
        self.config = {'root': str(self.root), 'hostId': 'sample', 'keepDaily': 14, 'maxAgeHours': 30,
                       'maxArchiveBytes': 1024 * 1024, 'minimumFreeBytes': 0, 'databases': {'sample': 'sqlite'}}
        self.receiver = R.Receiver(self.config)

    def tearDown(self):
        self.temp.cleanup()

    def upload(self, days=0, suffix='01234567', mutate=None):
        now = (R.utc_now() - dt.timedelta(days=days)).replace(microsecond=0)
        body = b'age-encryption.org/v1\nsynthetic-fixture'
        header = {'schemaVersion': 1, 'hostId': 'sample', 'stamp': now.strftime('%Y%m%dT%H%M%SZ') + '-' + suffix,
                  'createdAtUtc': now.isoformat(), 'sizeBytes': len(body), 'sha256': hashlib.sha256(body).hexdigest(),
                  'databases': [{'id': 'sample', 'provider': 'sqlite', 'capturedAtUtc': now.isoformat(), 'verified': True, 'sizeBytes': 100}],
                  'recoveredSecretCount': 0}
        if mutate:
            mutate(header)
        return self.receiver.receive(io.BytesIO(json.dumps(header).encode() + b'\n' + body))

    def test_complete_upload_is_published_with_bound_receipt_and_reverified(self):
        receipt = self.upload()
        status = self.receiver.status(verify=True)
        self.assertTrue(status['healthy'])
        self.assertTrue(status['latest']['checksumPass'])
        self.assertEqual(receipt['sha256'], status['latest']['sha256'])
        self.assertEqual([], list(self.receiver.incoming.iterdir()))

    def test_corrupt_upload_never_becomes_a_completed_recovery_point(self):
        with self.assertRaises(ValueError):
            self.upload(mutate=lambda h: h.update(sha256='0' * 64))
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
        archive = self.receiver.snapshots / receipt['stamp'] / 'recovery.tar.gz.age'
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


if __name__ == '__main__':
    unittest.main()
