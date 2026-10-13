#!/usr/bin/env python3
"""Restricted native database backup receiver, status, verification and retention."""
import argparse
import datetime as dt
import fcntl
import hashlib
import json
import os
import pathlib
import re
import shutil
import sys
import uuid

UTC = dt.timezone.utc
MARKER = 'powerforge-database-backup-v1'
STAMP = re.compile(r'^\d{8}T\d{6}Z-[0-9a-f]{8}$')


def utc_now():
    return dt.datetime.now(UTC)


def timestamp(value):
    result = dt.datetime.fromisoformat(value.replace('Z', '+00:00'))
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


def write_json(path, value):
    canonical(path)
    temporary = path.with_name(path.name + '.tmp-' + uuid.uuid4().hex)
    with temporary.open('x', encoding='utf-8') as stream:
        json.dump(value, stream, indent=2)
        stream.write('\n')
        stream.flush()
        os.fsync(stream.fileno())
    temporary.replace(path)


class Receiver:
    def __init__(self, config):
        self.config = config
        self.root = canonical(config['root'])
        if not self.root.is_dir() or (self.root / '.powerforge-database-backup-root').read_text().strip() != MARKER:
            raise ValueError('Backup root marker missing')
        if not 7 <= config['keepDaily'] <= 90 or not 1 <= config['maxAgeHours'] <= 168:
            raise ValueError('Invalid retention/freshness policy')
        if self.root.stat().st_mode & 0o077:
            raise ValueError('Backup root must be private')
        self.snapshots = canonical(self.root / 'snapshots')
        self.incoming = canonical(self.root / 'incoming')
        if not self.snapshots.is_dir() or not self.incoming.is_dir():
            raise ValueError('Backup storage directories missing')

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
        if shutil.disk_usage(self.root).free < header['sizeBytes'] + self.config['minimumFreeBytes']:
            raise OSError('Insufficient NAS space')
        final = canonical(self.snapshots / header['stamp'])
        if final.exists():
            raise ValueError('Capture already exists')
        partial = canonical(self.incoming / (header['stamp'] + '-' + uuid.uuid4().hex))
        partial.mkdir(mode=0o700)
        (partial / '.powerforge-incoming').write_text(MARKER)
        try:
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
            if manifest.get('schemaVersion') != 2 or manifest.get('format') != 'native' or manifest.get('databases') != header['databases'] or manifest.get('hostId') != header['hostId'] or manifest.get('stamp') != header['stamp']:
                raise ValueError('Native recovery manifest differs')
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
            partial.rename(final)
            fd = os.open(str(self.snapshots), os.O_RDONLY | os.O_DIRECTORY)
            try:
                os.fsync(fd)
            finally:
                os.close(fd)
            return receipt
        except BaseException:
            self.remove(partial, self.incoming, '.powerforge-incoming')
            raise

    def inspect(self, folder, verify=False):
        canonical(folder)
        if folder.parent != self.snapshots or not STAMP.fullmatch(folder.name):
            raise ValueError('Unsafe snapshot identity')
        for name in ('READY', 'capture.json', 'SHA256SUMS', 'VERIFIED', 'manifest.json'):
            if not canonical(folder / name).is_file():
                raise ValueError('Required snapshot file missing')
        if (folder / 'READY').read_text().strip() != MARKER:
            raise ValueError('Invalid completion marker')
        header = self.validate_header(json.loads((folder / 'capture.json').read_text()))
        receipt = json.loads((folder / 'VERIFIED').read_text())
        capture_hash = digest(folder / 'capture.json')
        if header['stamp'] != folder.name or receipt.get('stamp') != folder.name or receipt.get('verified') is not True or receipt.get('captureSha256') != capture_hash:
            raise ValueError('Receipt/capture identity differs')
        expected = ''.join(f['sha256'] + '  ' + f['entry'] + '\n' for f in header['files']) + capture_hash + '  capture.json\n'
        if (folder / 'SHA256SUMS').read_text() != expected or receipt.get('manifestSha256') != digest(folder / 'SHA256SUMS') or receipt.get('sizeBytes') != header['sizeBytes']:
            raise ValueError('Snapshot checksum metadata differs')
        for file in header['files']:
            path = canonical(folder / file['entry'])
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
        try:
            removed_partial = 0
            # Exclusive receiver lock establishes inactivity; retain recent or uncertain contents.
            for folder in self.incoming.iterdir():
                if re.fullmatch(r'\d{8}T\d{6}Z-[0-9a-f]{8}-[0-9a-f]{32}', folder.name) and utc_now().timestamp() - folder.stat().st_mtime > 86400:
                    self.remove(folder, self.incoming, '.powerforge-incoming')
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
                        keep.add(folder.name)
                        selected_days.add(day)
            removed = 0
            for folder in folders:
                if STAMP.fullmatch(folder.name) and folder.name not in keep:
                    self.remove(folder, self.snapshots, 'READY')
                    removed += 1
            result = {'healthy': True, 'startedAtUtc': started, 'completedAtUtc': utc_now().isoformat(),
                      'verifiedStamp': latest['latest']['stamp'], 'removed': removed, 'removedPartial': removed_partial,
                      'retainedDaily': len(selected_days)}
            write_json(self.root / 'maintenance.json', result)
            return result
        except Exception as exc:
            write_json(self.root / 'maintenance.json', {'healthy': False, 'startedAtUtc': started,
                       'completedAtUtc': utc_now().isoformat(), 'errorType': type(exc).__name__})
            raise


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
    lock_path = canonical(receiver.root / '.receiver.lock')
    with lock_path.open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        if operation == 'receive':
            result = receiver.receive(sys.stdin.buffer)
        elif operation == 'maintain':
            result = receiver.maintain()
        else:
            result = receiver.status(verify=operation == 'verify')
    print(json.dumps(result))
    return 0 if result.get('healthy', True) else 1


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception as error:
        print(json.dumps({'healthy': False, 'errorType': type(error).__name__}), file=sys.stderr)
        sys.exit(1)
