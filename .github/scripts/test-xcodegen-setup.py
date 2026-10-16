#!/usr/bin/env python3
"""Exercise the shipped setup script with isolated process-boundary fixtures."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'actions/setup-xcodegen/Install-XcodeGen.sh'
DIGEST = '4d9e34b62172d645eed6457cac13fc222569974098ef4ee9c3368bedf0196806'


class XcodeGenSetupContracts(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='powerforge-xcodegen-test-')
        self.root = Path(self.temporary.name)
        self.workspace = self.root / 'application'
        self.workspace.mkdir()
        (self.workspace / '.xcodegen-version').write_text('2.46.0\n')
        self.scratch = self.root / 'job scratch'
        self.scratch.mkdir()
        self.tools = self.root / 'tools'
        self.tools.mkdir()
        self.trace = self.root / 'trace'
        self.path_output = self.root / 'path'
        self.output = self.root / 'output'
        self.environment = dict(os.environ, GITHUB_WORKSPACE=str(self.workspace),
                                RUNNER_TEMP=str(self.scratch), GITHUB_PATH=str(self.path_output),
                                GITHUB_OUTPUT=str(self.output), FIXTURE_TRACE=str(self.trace),
                                FIXTURE_DIGEST=DIGEST, FIXTURE_VERSION='2.46.0',
                                PATH=str(self.tools) + os.pathsep + os.environ['PATH'])
        self.tool('uname', '#!/bin/sh\nprintf "Darwin\\n"\n')
        self.tool('curl', '''#!/bin/sh
echo download >> "$FIXTURE_TRACE"
if [ "${FIXTURE_DOWNLOAD_FAIL:-0}" = 1 ]; then exit 22; fi
while [ "$1" != --output ]; do shift; done
printf archive > "$2"
''')
        self.tool('shasum', '#!/bin/sh\nprintf "%s  archive\\n" "$FIXTURE_DIGEST"\n')
        self.tool('ditto', '''#!/bin/sh
echo extract >> "$FIXTURE_TRACE"
mkdir -p "$4/xcodegen/bin" "$4/xcodegen/share/xcodegen"
cat > "$4/xcodegen/bin/xcodegen" <<'TOOL'
#!/bin/sh
echo execute >> "$FIXTURE_TRACE"
printf 'Version: %s\\n' "$FIXTURE_VERSION"
TOOL
printf presets > "$4/xcodegen/share/xcodegen/presets"
''')

    def tearDown(self):
        self.temporary.cleanup()

    def tool(self, name, source):
        path = self.tools / name
        path.write_text(source)
        path.chmod(0o755)

    def run_setup(self):
        return subprocess.run(['bash', str(SCRIPT)], env=self.environment,
                              capture_output=True, text=True, timeout=10)

    def assert_rejected(self, message):
        result = self.run_setup()
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertIn(message, result.stderr)
        self.assertFalse(self.path_output.exists())
        self.assertFalse(self.output.exists())
        self.assertEqual(list(self.scratch.iterdir()), [])

    def test_old_and_new_global_versions_are_not_used_or_changed(self):
        for version in ['2.45.0', '2.99.0']:
            with self.subTest(version=version):
                self.tool('xcodegen', f'#!/bin/sh\necho global-tool-used >> "$FIXTURE_TRACE"\nprintf "Version: {version}\\n"\n')
                original = (self.tools / 'xcodegen').read_bytes()
                result = self.run_setup()
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual((self.tools / 'xcodegen').read_bytes(), original)
                self.assertNotIn('global-tool-used', result.stderr)
                self.assertNotIn('global-tool-used', self.trace.read_text())
                path = self.path_output.read_text().splitlines()[-1]
                self.assertTrue((Path(path) / 'xcodegen').is_file())
                self.assertTrue((Path(path).parent / 'share/xcodegen/presets').is_file())
                self.assertIn('version=2.46.0', self.output.read_text())

    def test_missing_global_tool_and_crlf_pin_are_supported(self):
        (self.workspace / '.xcodegen-version').write_bytes(b'2.46.0\r\n')
        self.environment['PATH'] = str(self.tools) + os.pathsep + '/usr/bin:/bin'
        self.assertEqual(self.run_setup().returncode, 0)

    def test_unknown_pin_stops_before_download(self):
        (self.workspace / '.xcodegen-version').write_text('2.99.0\n')
        self.assert_rejected('Unsupported XcodeGen version')
        self.assertFalse(self.trace.exists())

    def test_checksum_failure_stops_before_extraction_or_execution(self):
        self.environment['FIXTURE_DIGEST'] = '0' * 64
        self.assert_rejected('checksum does not match')
        self.assertEqual(self.trace.read_text(), 'download\n')

    def test_download_failure_cleans_partial_installation(self):
        self.environment['FIXTURE_DOWNLOAD_FAIL'] = '1'
        self.assert_rejected('')
        self.assertEqual(self.trace.read_text(), 'download\n')

    def test_unexpected_binary_version_is_not_published(self):
        self.environment['FIXTURE_VERSION'] = '2.99.0'
        self.assert_rejected('unexpected version')
        self.assertEqual(self.trace.read_text(), 'download\nextract\nexecute\n')

    def test_linked_version_file_is_rejected(self):
        version = self.workspace / '.xcodegen-version'
        version.unlink()
        version.symlink_to(self.root / 'other-version')
        (self.root / 'other-version').write_text('2.46.0\n')
        self.assert_rejected('regular file')
        self.assertFalse(self.trace.exists())


if __name__ == '__main__':
    unittest.main()
