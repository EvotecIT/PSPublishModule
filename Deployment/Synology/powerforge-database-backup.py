#!/usr/bin/env python3
"""Restricted native database backup receiver, status, verification and retention."""
import argparse
import contextlib
import datetime as dt
import fcntl
import hashlib
import json
import os
import pathlib
import re
import shutil
import signal
import sys
import uuid

UTC = dt.timezone.utc
MARKER = 'powerforge-database-backup-v1'
STAMP = re.compile(r'^\d{8}T\d{6}Z-[0-9a-f]{8}$')
MAX_METADATA_BYTES = 1024 * 1024


def portable_file_id(value):
    return (isinstance(value, str) and re.fullmatch(r'[a-z][a-z0-9-]{0,31}', value) is not None and
            re.fullmatch(r'con|prn|aux|nul|com[1-9]|lpt[1-9]', value) is None)


def utc_now():
    return dt.datetime.now(UTC)


def timestamp(value):
    # DSM Python 3.8 accepts only three or six fractional digits; .NET emits up to seven.
    value = value.replace('Z', '+00:00')
    value = re.sub(r'\.(\d+)(?=[+-]\d{2}:\d{2}$)',
                   lambda match: '.' + match.group(1).ljust(6, '0')[:6], value)
    result = dt.datetime.fromisoformat(value)
    if result.tzinfo is None:
        raise ValueError('Timestamp must include its UTC offset')
    return result.astimezone(UTC)


def canonical(path):
    path = pathlib.Path(path)
    if not path.is_absolute() or path.resolve() != path:
        raise ValueError('Path must be absolute and physical')
    for item in [path] + list(path.parents):
        if item.is_symlink():
            raise ValueError('Symlink in backup path')
    return path


def digest(path):
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(block)
    return value.hexdigest()


def sync_directory(path):
    descriptor = os.open(str(canonical(path)), os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def write_json(path, value):
    canonical(path)
    temporary = path.with_name(path.name + '.tmp-' + uuid.uuid4().hex)
    with temporary.open('x', encoding='utf-8') as stream:
        json.dump(value, stream, indent=2)
        stream.write('\n')
        stream.flush()
        os.fsync(stream.fileno())
    temporary.replace(path)
    sync_directory(path.parent)


class Receiver:
    def __init__(self, config):
        self.config = config
        if not isinstance(config.get('hostId'), str) or re.fullmatch(r'[a-z0-9][a-z0-9-]{0,63}', config['hostId']) is None:
            raise ValueError('A portable lowercase host identity is required')
        if (type(config.get('minimumFreeBytes')) is not int or config['minimumFreeBytes'] < 0 or
                type(config.get('maxArchiveBytes')) is not int or config['maxArchiveBytes'] < 1):
            raise ValueError('Capacity limit and free-space reserve must be non-negative integer byte counts')
        databases = config.get('databases')
        configurations = config.get('configurationIds', [])
        if (not isinstance(databases, dict) or not databases or
                any(not portable_file_id(identity) or provider not in ('sqlite', 'sqlserver') for identity, provider in databases.items()) or
                not isinstance(configurations, list) or any(not portable_file_id(identity) for identity in configurations) or
                len(set(configurations)) != len(configurations)):
            raise ValueError('A non-empty portable database inventory and unique recovery IDs are required')
        self.owner = os.geteuid()
        self.root = canonical(config['root'])
        if not self.root.is_dir():
            raise ValueError('Backup root missing')
        self.demand_managed(self.root)
        self.demand_managed(self.root / '.powerforge-database-backup-root')
        if (self.root / '.powerforge-database-backup-root').read_text().strip() != MARKER:
            raise ValueError('Backup root marker missing')
        if not 7 <= config['keepDaily'] <= 90 or not 1 <= config['maxAgeHours'] <= 168:
            raise ValueError('Invalid retention/freshness policy')
        self.receive_timeout_seconds = config.get('receiveTimeoutSeconds', 18000)
        if type(self.receive_timeout_seconds) is not int or not 1 <= self.receive_timeout_seconds <= 86400:
            raise ValueError('Invalid receive time budget')
        self.snapshots = canonical(self.root / 'snapshots')
        self.incoming = canonical(self.root / 'incoming')
        if not self.snapshots.is_dir() or not self.incoming.is_dir():
            raise ValueError('Backup storage directories missing')
        if self.snapshots.stat().st_dev != self.incoming.stat().st_dev:
            raise ValueError('Incoming and snapshots must share a filesystem for atomic publication')
        for path in (self.snapshots, self.incoming, self.root / '.powerforge-database-backup-root', self.root / '.receiver.lock', self.root / '.catalog.lock'):
            self.demand_managed(path)

    def demand_managed(self, path):
        canonical(path)
        metadata = path.stat()
        if metadata.st_uid != self.owner or metadata.st_mode & 0o077:
            raise ValueError('Managed backup storage must be private and owned by the receiver identity')

    def validate_manifest(self, manifest, header):
        expected = [file for file in header['files'] if file['entry'].startswith('config/')]
        files = manifest.get('files', [])
        if not isinstance(files, list) or not all(isinstance(file, dict) for file in files):
            raise ValueError('Invalid recovery file manifest')
        if (manifest.get('schemaVersion') != 2 or manifest.get('format') != 'native' or
                manifest.get('databases') != header['databases'] or manifest.get('hostId') != header['hostId'] or
                manifest.get('stamp') != header['stamp'] or timestamp(manifest['createdAtUtc']) != timestamp(header['createdAtUtc']) or
                sorted(files, key=lambda file: file.get('entry', '')) != sorted(expected, key=lambda file: file['entry'])):
            raise ValueError('Native recovery manifest differs')

    @contextlib.contextmanager
    def catalog_guard(self, exclusive=False):
        # Upload bytes and checksum reads never hold this short publication/deletion guard.
        with canonical(self.root / '.catalog.lock').open('r+') as lock:
            fcntl.flock(lock, fcntl.LOCK_EX if exclusive else fcntl.LOCK_SH)
            yield

    def validate_header(self, header):
        if header.get('schemaVersion') != 2 or header.get('format') != 'native' or header.get('hostId') != self.config['hostId'] or not STAMP.fullmatch(header.get('stamp', '')):
            raise ValueError('Unexpected source/capture identity')
        if type(header.get('sizeBytes')) is not int or not 1 <= header['sizeBytes'] <= self.config['maxArchiveBytes']:
            raise ValueError('Invalid backup set size')
        created = timestamp(header['createdAtUtc'])
        stamped = dt.datetime.strptime(header['stamp'][:16], '%Y%m%dT%H%M%SZ').replace(tzinfo=UTC)
        if abs((created - stamped).total_seconds()) >= 1 or (created - utc_now()).total_seconds() > 300:
            raise ValueError('Capture timestamp is inconsistent or future-dated')
        databases = header.get('databases', [])
        expected = self.config['databases']
        if len(databases) != len(expected) or {v.get('id'): v.get('provider') for v in databases} != expected:
            raise ValueError('Required database set differs')
        expected_entries = {identity + ('.sqlite' if provider == 'sqlite' else '.bak') for identity, provider in expected.items()}
        expected_entries |= {'config/' + identity + '.json' for identity in self.config.get('configurationIds', [])}
        expected_entries.add('manifest.json')
        files = header.get('files', [])
        if len(files) != len(expected_entries) or {f.get('entry') for f in files} != expected_entries:
            raise ValueError('Required native file set differs')
        for file in files:
            if type(file.get('sizeBytes')) is not int or file['sizeBytes'] <= 0 or not re.fullmatch('[a-f0-9]{64}', file.get('sha256', '')):
                raise ValueError('Invalid native file metadata')
            if file['entry'] == 'manifest.json' and file['sizeBytes'] > MAX_METADATA_BYTES:
                raise ValueError('Native recovery metadata size exceeds its bound')
        if sum(f['sizeBytes'] for f in files) != header['sizeBytes']:
            raise ValueError('Native file size inventory differs')
        inventory = {f['entry']: f for f in files}
        for db in databases:
            captured = timestamp(db['capturedAtUtc'])
            entry = db['id'] + ('.sqlite' if db['provider'] == 'sqlite' else '.bak')
            if db.get('verified') is not True or db.get('entry') != entry or any(db.get(k) != inventory[entry][k] for k in ('sizeBytes', 'sha256')):
                raise ValueError('Unverified or mismatched database capture')
            if captured < created or captured > utc_now() + dt.timedelta(minutes=5):
                raise ValueError('Invalid database capture time')
        return header

    def receive(self, stream):
        line = stream.readline(65537)
        if len(line) > 65536 or not line.endswith(b'\n'):
            raise ValueError('Invalid protocol header')
        header = self.validate_header(json.loads(line))
        if (utc_now() - timestamp(header['createdAtUtc'])).total_seconds() > self.config['maxAgeHours'] * 3600:
            raise ValueError('New uploads must be within the capture freshness window')
        if shutil.disk_usage(self.incoming).free < header['sizeBytes'] + self.config['minimumFreeBytes']:
            raise OSError('Insufficient NAS space')
        final = canonical(self.snapshots / header['stamp'])
        if final.exists():
            raise ValueError('Capture already exists')
        partial = canonical(self.incoming / (header['stamp'] + '-' + uuid.uuid4().hex))
        created = False
        try:
            partial.mkdir(mode=0o700)
            created = True
            (partial / '.powerforge-incoming').write_text(MARKER)
            for file in header['files']:
                path = canonical(partial / file['entry'])
                path.parent.mkdir(mode=0o700, exist_ok=True)
                remaining = file['sizeBytes']
                with path.open('xb') as destination:
                    while remaining:
                        block = stream.read(min(1024 * 1024, remaining))
                        if not block:
                            raise ValueError('Truncated native file upload')
                        destination.write(block)
                        remaining -= len(block)
                    destination.flush()
                    os.fsync(destination.fileno())
                if digest(path) != file['sha256']:
                    raise ValueError('NAS native file hash mismatch')
            if stream.read(1):
                raise ValueError('Unexpected trailing upload data')
            manifest = json.loads((partial / 'manifest.json').read_text())
            self.validate_manifest(manifest, header)
            (partial / 'capture.json').write_bytes(line[:-1])
            checksums = ''.join(f['sha256'] + '  ' + f['entry'] + '\n' for f in header['files'])
            checksums += digest(partial / 'capture.json') + '  capture.json\n'
            (partial / 'SHA256SUMS').write_text(checksums)
            receipt = {'stamp': header['stamp'], 'captureSha256': digest(partial / 'capture.json'), 'sizeBytes': header['sizeBytes'],
                       'verified': True, 'verifiedAtUtc': utc_now().isoformat(), 'manifestSha256': digest(partial / 'SHA256SUMS')}
            write_json(partial / 'VERIFIED', receipt)
            (partial / 'READY').write_text(MARKER + '\n')
            for name in ('capture.json', 'SHA256SUMS', 'VERIFIED', 'READY'):
                with (partial / name).open('rb') as committed:
                    os.fsync(committed.fileno())
            for directory, _, _ in os.walk(str(partial), topdown=False, followlinks=False):
                sync_directory(pathlib.Path(directory))
            with self.catalog_guard(exclusive=True):
                partial.rename(final)
                sync_directory(self.incoming)
                sync_directory(self.snapshots)
            return receipt
        except BaseException:
            # After rename, preserve the verified snapshot and the original publication error.
            if created and partial.exists():
                self.remove_incoming(partial)
            raise

    def remove_incoming(self, folder):
        """Remove a marked upload or an interrupted, empty marker initialization only."""
        self.demand_managed(folder)
        if folder.parent != self.incoming or re.fullmatch(r'\d{8}T\d{6}Z-[0-9a-f]{8}-[0-9a-f]{32}', folder.name) is None:
            raise ValueError('Invalid incoming cleanup identity')
        marker = folder / '.powerforge-incoming'
        entries = list(folder.iterdir())
        if not entries:
            folder.rmdir()
        elif entries == [marker]:
            self.demand_managed(marker)
            if not marker.is_file() or marker.stat().st_size > len(MARKER) or not MARKER.startswith(marker.read_text()):
                raise ValueError('Unrecognized partial marker; cleanup held')
            marker.unlink()
            folder.rmdir()
        else:
            self.remove(folder, self.incoming, '.powerforge-incoming')

    def inspect(self, folder, verify=False):
        canonical(folder)
        self.demand_managed(folder)
        if folder.parent != self.snapshots or not STAMP.fullmatch(folder.name):
            raise ValueError('Unsafe snapshot identity')
        for name in ('READY', 'capture.json', 'SHA256SUMS', 'VERIFIED', 'manifest.json'):
            if not canonical(folder / name).is_file():
                raise ValueError('Required snapshot file missing')
            self.demand_managed(folder / name)
            limit = 65536 if name == 'capture.json' else MAX_METADATA_BYTES
            if (folder / name).stat().st_size > limit:
                raise ValueError('Stored recovery metadata size exceeds its bound')
        if (folder / 'READY').read_text().strip() != MARKER:
            raise ValueError('Invalid completion marker')
        header = self.validate_header(json.loads((folder / 'capture.json').read_text()))
        self.validate_manifest(json.loads((folder / 'manifest.json').read_text()), header)
        receipt = json.loads((folder / 'VERIFIED').read_text())
        capture_hash = digest(folder / 'capture.json')
        if header['stamp'] != folder.name or receipt.get('stamp') != folder.name or receipt.get('verified') is not True or receipt.get('captureSha256') != capture_hash:
            raise ValueError('Receipt/capture identity differs')
        expected = ''.join(f['sha256'] + '  ' + f['entry'] + '\n' for f in header['files']) + capture_hash + '  capture.json\n'
        if (folder / 'SHA256SUMS').read_text() != expected or receipt.get('manifestSha256') != digest(folder / 'SHA256SUMS') or receipt.get('sizeBytes') != header['sizeBytes']:
            raise ValueError('Snapshot checksum metadata differs')
        for file in header['files']:
            path = canonical(folder / file['entry'])
            self.demand_managed(path.parent)
            self.demand_managed(path)
            if not path.is_file() or path.stat().st_size != file['sizeBytes']:
                raise ValueError('Native backup file missing or size differs')
            if verify and digest(path) != file['sha256']:
                raise ValueError('Stored native file hash mismatch')
        verified = timestamp(receipt['verifiedAtUtc'])
        if verified < timestamp(header['createdAtUtc']) or verified > utc_now() + dt.timedelta(minutes=5):
            raise ValueError('Invalid verification time')
        return dict(header, verifiedAtUtc=receipt['verifiedAtUtc'], verified=True, captureSha256=capture_hash,
                    manifestSha256=receipt['manifestSha256'], checksumPass=verify)

    def status(self, verify=False):
        folders = sorted(p for p in self.snapshots.iterdir() if STAMP.fullmatch(p.name))
        if not folders:
            raise ValueError('No completed database backup')
        latest = self.inspect(folders[-1], verify)
        age = (utc_now() - timestamp(latest['createdAtUtc'])).total_seconds()
        if (self.root / 'maintenance.json').exists():
            self.demand_managed(self.root / 'maintenance.json')
        return {'healthy': -300 <= age <= self.config['maxAgeHours'] * 3600, 'latest': latest, 'ageSeconds': age,
                'snapshotCount': len(folders), 'dailyCount': len({p.name[:8] for p in folders}),
                'keepDaily': self.config['keepDaily'], 'maxAgeHours': self.config['maxAgeHours'],
                'maintenance': json.loads((self.root / 'maintenance.json').read_text()) if (self.root / 'maintenance.json').exists() else None}

    @staticmethod
    def remove(folder, parent, marker):
        canonical(folder)
        if folder.parent != parent or (folder / marker).read_text().strip() != MARKER:
            raise ValueError('Refusing deletion outside marked backup storage')
        for directory, subdirectories, files in os.walk(str(folder), followlinks=False):
            for name in subdirectories + files:
                canonical(pathlib.Path(directory) / name)
        shutil.rmtree(folder)

    def maintain(self):
        started = utc_now().isoformat()
        if (self.root / 'maintenance.json').exists():
            self.demand_managed(self.root / 'maintenance.json')
        previous = json.loads((self.root / 'maintenance.json').read_text()) if (self.root / 'maintenance.json').exists() else {}
        pinned = previous.get('dailyKeepers', {})
        if not isinstance(pinned, dict):
            raise ValueError('Invalid retained daily point record')
        try:
            removed_partial = 0
            # Exclusive receiver lock establishes inactivity; retain recent or uncertain contents.
            for folder in self.incoming.iterdir():
                if re.fullmatch(r'\d{8}T\d{6}Z-[0-9a-f]{8}-[0-9a-f]{32}', folder.name) and utc_now().timestamp() - folder.stat().st_mtime > 86400:
                    self.remove_incoming(folder)
                    removed_partial += 1
            latest = self.status(verify=True)
            if not latest['healthy']:
                raise ValueError('Latest backup is stale; retention held')
            folders = sorted(self.snapshots.iterdir(), reverse=True)
            selected_days, keep = set(), set()
            for folder in folders:
                if STAMP.fullmatch(folder.name):
                    self.inspect(folder)
                    day = folder.name[:8]
                    if day not in selected_days and len(selected_days) < self.config['keepDaily']:
                        name = pinned.get(day, folder.name)
                        if not isinstance(name, str) or not STAMP.fullmatch(name) or name[:8] != day:
                            raise ValueError('Invalid retained daily point identity')
                        self.inspect(self.snapshots / name, verify=name != latest['latest']['stamp'])
                        keep.add(name)
                        selected_days.add(day)
            daily_keepers = {name[:8]: name for name in keep}
            # An upload cannot replace a point already adopted by trusted maintenance.
            # Also retain the newest receipt so normal same-day retries remain monitorable.
            keep.add(latest['latest']['stamp'])
            removals = [folder for folder in folders if STAMP.fullmatch(folder.name) and folder.name not in keep]
            removed = 0
            with self.catalog_guard(exclusive=True):
                for folder in removals:
                    self.remove(folder, self.snapshots, 'READY')
                    removed += 1
                sync_directory(self.snapshots)
            result = {'healthy': True, 'startedAtUtc': started, 'completedAtUtc': utc_now().isoformat(),
                      'verifiedStamp': latest['latest']['stamp'], 'removed': removed, 'removedPartial': removed_partial,
                      'retainedDaily': len(selected_days), 'dailyKeepers': daily_keepers}
            write_json(self.root / 'maintenance.json', result)
            return result
        except Exception as exc:
            write_json(self.root / 'maintenance.json', {'healthy': False, 'startedAtUtc': started,
                       'completedAtUtc': utc_now().isoformat(), 'errorType': type(exc).__name__, 'dailyKeepers': pinned})
            raise


@contextlib.contextmanager
def receive_deadline(seconds):
    """Bound header, body and publication time independently of the SSH client."""
    def expired(signum, frame):
        raise TimeoutError('NAS receive time budget exceeded')
    previous_handler = signal.signal(signal.SIGALRM, expired)
    signal.setitimer(signal.ITIMER_REAL, seconds)
    try:
        yield
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0)
        signal.signal(signal.SIGALRM, previous_handler)


def dispatch(receiver, operation, stream=None):
    if operation == 'status':
        with receiver.catalog_guard():
            return receiver.status()
    lock_path = canonical(receiver.root / '.receiver.lock')
    with lock_path.open('r+') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        if operation == 'receive':
            with receive_deadline(receiver.receive_timeout_seconds):
                return receiver.receive(stream)
        if operation == 'maintain':
            return receiver.maintain()
        with receiver.catalog_guard():
            return receiver.status(verify=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--config', required=True)
    parser.add_argument('operation', choices=['receive', 'status', 'verify', 'maintain', 'forced'])
    args = parser.parse_args()
    if os.geteuid() != 0:
        raise PermissionError('Receiver requires its managed root identity')
    os.umask(0o077)
    config_path = canonical(args.config)
    if config_path.stat().st_uid != 0 or config_path.stat().st_mode & 0o022:
        raise PermissionError('Receiver configuration must be root-owned and non-writable by other users')
    receiver = Receiver(json.loads(config_path.read_text()))
    operation = args.operation
    if operation == 'forced':
        if os.environ.get('SSH_ORIGINAL_COMMAND') != 'receive':
            raise PermissionError('Restricted key permits only receive')
        operation = 'receive'
    result = dispatch(receiver, operation, sys.stdin.buffer)
    print(json.dumps(result))
    return 0 if result.get('healthy', True) else 1


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception as error:
        print(json.dumps({'healthy': False, 'errorType': type(error).__name__}), file=sys.stderr)
        sys.exit(1)
