import datetime as dt
import importlib.util
import pathlib
import unittest

spec = importlib.util.spec_from_file_location('health', pathlib.Path(__file__).with_name('verify-database-backups.py'))
H = importlib.util.module_from_spec(spec)
spec.loader.exec_module(H)


class HealthContracts(unittest.TestCase):
    def fixture(self):
        now = dt.datetime.now(dt.timezone.utc)
        stamp = now.isoformat()
        local = {'status': 'success', 'stamp': 'capture', 'archiveSha256': 'hash'}
        nas = {'healthy': True, 'keepDaily': 14, 'maxAgeHours': 30, 'snapshotCount': 1,
               'latest': {'verified': True, 'manifestSha256': 'manifest', 'stamp': 'capture', 'sha256': 'hash',
                          'databases': [{'id': k, 'provider': v, 'verified': True, 'sizeBytes': 100, 'capturedAtUtc': stamp} for k, v in H.EXPECTED.items()]},
               'maintenance': {'healthy': True, 'completedAtUtc': stamp}}
        task = {'enabled': True, 'daily': True, 'expectedAction': True, 'lastResult': 0,
                'nextRunUtc': (now + dt.timedelta(hours=20)).isoformat(), 'currentRun': {'status': 'success'}}
        restore = {'healthy': True, 'databaseIds': list(H.EXPECTED), 'completedAtUtc': stamp}
        return local, nas, task, {'healthy': True}, restore, now

    def test_initial_single_recovery_point_is_healthy_while_history_accumulates(self):
        result = H.evaluate(*self.fixture())
        self.assertTrue(result['healthy'])
        self.assertEqual(3, len(result['databases']))

    def test_failed_latest_attempt_is_not_hidden_by_previous_success(self):
        values = self.fixture()
        values[2]['currentRun'] = {'status': 'failed', 'stage': 'encrypt'}
        self.assertFalse(H.evaluate(*values)['healthy'])

    def test_running_job_with_fresh_verified_prior_capture_is_healthy(self):
        values = self.fixture()
        values[2].update(running=True, lastResult=267009, currentRun={'status': 'running', 'startedAtUtc': values[-1].isoformat()})
        self.assertTrue(H.evaluate(*values)['healthy'])

    def test_stale_database_and_disabled_schedule_remain_failures(self):
        values = self.fixture()
        values[1]['latest']['databases'][0]['capturedAtUtc'] = (values[-1] - dt.timedelta(hours=31)).isoformat()
        values[2]['enabled'] = False
        self.assertFalse(H.evaluate(*values)['healthy'])

    def test_missing_restore_or_nas_verification_is_not_healthy(self):
        values = self.fixture()
        values[1]['maintenance'] = {}
        values[4].clear()
        self.assertFalse(H.evaluate(*values)['healthy'])


if __name__ == '__main__':
    unittest.main()
