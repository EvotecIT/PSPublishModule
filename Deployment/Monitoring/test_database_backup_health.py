import datetime as dt
import importlib.util
import pathlib
import json
import subprocess
import sys
import unittest

spec = importlib.util.spec_from_file_location('health', pathlib.Path(__file__).with_name('verify-database-backups.py'))
H = importlib.util.module_from_spec(spec)
spec.loader.exec_module(H)


class HealthContracts(unittest.TestCase):
    def fixture(self):
        now = dt.datetime.now(dt.timezone.utc)
        stamp = now.isoformat()
        local = {'status': 'success', 'stamp': 'capture', 'captureSha256': 'hash'}
        nas = {'healthy': True, 'keepDaily': 14, 'maxAgeHours': 30, 'snapshotCount': 1,
               'latest': {'verified': True, 'manifestSha256': 'manifest', 'stamp': 'capture', 'captureSha256': 'hash',
                          'databases': [{'id': k, 'provider': v, 'verified': True, 'sizeBytes': 100, 'capturedAtUtc': stamp} for k, v in H.EXPECTED.items()]},
               'maintenance': {'healthy': True, 'completedAtUtc': stamp}}
        task = {'enabled': True, 'daily': True, 'expectedAction': True, 'lastResult': 0, 'runBudgetMinutes': 300,
                'nextRunUtc': (now + dt.timedelta(hours=20)).isoformat(), 'currentRun': {'status': 'success'}}
        restore = {'healthy': True, 'databaseIds': list(H.EXPECTED), 'completedAtUtc': stamp}
        return local, nas, task, {'healthy': True}, restore, now

    def test_initial_single_recovery_point_is_healthy_while_history_accumulates(self):
        result = H.evaluate(*self.fixture())
        self.assertTrue(result['healthy'])
        self.assertEqual(3, len(result['databases']))

    def test_dotnet_fractional_timestamps_work_for_all_health_time_boundaries(self):
        values = self.fixture()
        def dotnet(value):
            return value.strftime('%Y-%m-%dT%H:%M:%S') + '.1234567+00:00'
        values[2].update(nextRunUtc=dotnet(values[-1] + dt.timedelta(hours=20)), running=True,
                         currentRun={'status': 'running', 'startedAtUtc': dotnet(values[-1] - dt.timedelta(hours=1))})
        values[4]['completedAtUtc'] = dotnet(values[-1] - dt.timedelta(hours=1))
        for database in values[1]['latest']['databases']:
            database['capturedAtUtc'] = dotnet(values[-1] - dt.timedelta(hours=1))
        self.assertTrue(H.evaluate(*values)['healthy'])
        self.assertEqual(123456, H.timestamp('2026-10-13T11:00:00.1234567Z').microsecond)

    def test_failed_latest_attempt_is_not_hidden_by_previous_success(self):
        values = self.fixture()
        values[2]['currentRun'] = {'status': 'failed', 'stage': 'transfer'}
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

    def test_manual_success_with_cleanup_failure_is_visible(self):
        values = self.fixture()
        values[2]['currentRun'] = {'status': 'success', 'cleanupErrorType': 'IOException'}
        self.assertFalse(H.evaluate(*values)['healthy'])

    def test_configured_long_run_budget_does_not_fail_at_the_default_limit(self):
        values = self.fixture()
        values[2].update(running=True, runBudgetMinutes=660, currentRun={'status': 'running', 'startedAtUtc': (values[-1] - dt.timedelta(hours=6)).isoformat()})
        self.assertTrue(H.evaluate(*values)['healthy'])
        values[2]['currentRun']['startedAtUtc'] = (values[-1] - dt.timedelta(hours=12)).isoformat()
        self.assertFalse(H.evaluate(*values)['healthy'])

    @unittest.skipUnless(sys.platform == 'win32', 'Uses Windows Task Scheduler value types')
    def test_custom_installation_is_checked_and_changed_action_is_detected(self):
        policy = {'schemaVersion': 1, 'installRoot': "D:\\Backup ' recovery", 'taskName': "Custom 'backup", 'dailyAt': '04:35', 'timeoutMinutes': 600, 'taskLimitMinutes': 660}
        encoded = json.dumps(policy).replace("'", "''")
        fixture = "$policy='" + encoded + "'|ConvertFrom-Json;function Get-ScheduledTask { param($TaskName);if($TaskName -ne $policy.taskName){throw 'Wrong task queried'};[pscustomobject]@{State='Ready';Triggers=@([pscustomobject]@{DaysInterval=1;StartBoundary='2026-01-01T04:35:00'});Actions=@([pscustomobject]@{Execute=($policy.installRoot+'\\app\\PowerForge.Backups.Cli.exe');Arguments=('run \"'+$policy.installRoot+'\\config.json\"')});Principal=[pscustomobject]@{UserId='SYSTEM'};Settings=[pscustomobject]@{ExecutionTimeLimit='PT11H'}}};function Get-ScheduledTaskInfo { [pscustomobject]@{LastTaskResult=0;NextRunTime=([datetime]::UtcNow.AddHours(20))} };"
        fixture = "$culture=[Globalization.CultureInfo]::GetCultureInfo('th-TH').Clone();$culture.DateTimeFormat.TimeSeparator='.';[Threading.Thread]::CurrentThread.CurrentCulture=$culture;" + fixture
        for limit, expected in [(660, True), (659, False)]:
            command = H.windows_schedule_command(dict(policy, timeoutMinutes=limit-60, taskLimitMinutes=limit))
            process = subprocess.run(['powershell.exe', '-NoProfile', '-NonInteractive', '-Command', fixture + command], capture_output=True, timeout=30)
            self.assertEqual(0, process.returncode, process.stderr.decode(errors='replace'))
            result = json.loads(process.stdout.decode('utf-8-sig'))
            self.assertTrue(result['daily'])
            self.assertEqual(expected, result['expectedAction'])
            self.assertEqual(660, result['runBudgetMinutes'])


if __name__ == '__main__':
    unittest.main()
