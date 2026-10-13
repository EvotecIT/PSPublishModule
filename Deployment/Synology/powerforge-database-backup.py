#!/usr/bin/env python3
"""Restricted encrypted database backup receiver, status, verification and retention."""
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
        for folder in [self.snapshots, self.incoming]:
            folder.mkdir(mode=0o700, exist_ok=True)

    def validate_header(self, header):
        if header.get('schemaVersion') != 1 or header.get('hostId') != self.config['hostId'] or not STAMP.fullmatch(header.get('stamp', '')):
            raise ValueError('Unexpected source/capture identity')
        if not re.fullmatch(r'[a-f0-9]{64}', header.get('sha256', '')):
            raise ValueError('Invalid archive digest')
        if type(header.get('sizeBytes')) is not int or not 1 <= header['sizeBytes'] <= self.config['maxArchiveBytes']:
            raise ValueError('Invalid archive size')
        created = timestamp(header['createdAtUtc'])
        stamped = dt.datetime.strptime(header['stamp'][:16], '%Y%m%dT%H%M%SZ').replace(tzinfo=UTC)
        if abs((created - stamped).total_seconds()) >= 1 or (created - utc_now()).total_seconds() > 300:
            raise ValueError('Capture timestamp is inconsistent or future-dated')
        databases = header.get('databases', [])
        expected = self.config['databases']
        if len(databases) != len(expected) or {v.get('id'): v.get('provider') for v in databases} != expected:
            raise ValueError('Required database set differs')
        for db in databases:
            captured = timestamp(db['capturedAtUtc'])
            if db.get('verified') is not True or type(db.get('sizeBytes')) is not int or db['sizeBytes'] <= 0:
                raise ValueError('Unverified or empty database capture')
            if captured < created or captured > utc_now() + dt.timedelta(minutes=5):
                raise ValueError('Invalid database capture time')
        if type(header.get('recoveredSecretCount')) is not int or header['recoveredSecretCount'] < self.config.get('minimumRecoveredSecrets', 0):
            raise ValueError('Required recovery material missing')
        return {k: header[k] for k in ('schemaVersion', 'hostId', 'stamp', 'createdAtUtc', 'sizeBytes', 'sha256', 'databases', 'recoveredSecretCount')}

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
            archive = partial / 'recovery.tar.gz.age'
            remaining = header['sizeBytes']
            with archive.open('xb') as destination:
                first = True
                while remaining:
                    block = stream.read(min(1024 * 1024, remaining))
                    if not block:
                        raise ValueError('Truncated upload')
                    if first and not block.startswith(b'age-encryption.org/v1\n'):
                        raise ValueError('Upload is not an age encrypted archive')
                    first = False
                    destination.write(block)
                    remaining -= len(block)
                if stream.read(1):
                    raise ValueError('Unexpected trailing upload data')
                destination.flush()
                os.fsync(destination.fileno())
            if digest(archive) != header['sha256']:
                raise ValueError('NAS archive hash mismatch')
            write_json(partial / 'capture.json', header)
            checksums = header['sha256'] + '  recovery.tar.gz.age\n' + digest(partial / 'capture.json') + '  capture.json\n'
            (partial / 'SHA256SUMS').write_text(checksums)
            receipt = {'stamp': header['stamp'], 'sha256': header['sha256'], 'sizeBytes': header['sizeBytes'],
                       'verified': True, 'verifiedAtUtc': utc_now().isoformat(),
                       'manifestSha256': digest(partial / 'SHA256SUMS')}
            write_json(partial / 'VERIFIED', receipt)
            (partial / 'READY').write_text(MARKER + '\n')
            for item in partial.iterdir():
                with item.open('rb') as committed:
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
        for name in ('READY', 'capture.json', 'SHA256SUMS', 'VERIFIED', 'recovery.tar.gz.age'):
            path = canonical(folder / name)
            if not path.is_file():
                raise ValueError('Required snapshot file missing')
        if (folder / 'READY').read_text().strip() != MARKER:
            raise ValueError('Invalid completion marker')
        header = self.validate_header(json.loads((folder / 'capture.json').read_text()))
        receipt = json.loads((folder / 'VERIFIED').read_text())
        if header['stamp'] != folder.name or receipt.get('stamp') != folder.name or receipt.get('verified') is not True:
            raise ValueError('Receipt/capture identity differs')
        if receipt.get('manifestSha256') != digest(folder / 'SHA256SUMS'):
            raise ValueError('Receipt manifest binding differs')
        expected = header['sha256'] + '  recovery.tar.gz.age\n' + digest(folder / 'capture.json') + '  capture.json\n'
        if (folder / 'SHA256SUMS').read_text() != expected or receipt.get('sha256') != header['sha256'] or receipt.get('sizeBytes') != header['sizeBytes']:
            raise ValueError('Snapshot checksum metadata differs')
        if (folder / 'recovery.tar.gz.age').stat().st_size != header['sizeBytes']:
            raise ValueError('Archive size differs')
        verified = timestamp(receipt['verifiedAtUtc'])
        if verified < timestamp(header['createdAtUtc']) or verified > utc_now() + dt.timedelta(minutes=5):
            raise ValueError('Invalid verification time')
        if verify and digest(folder / 'recovery.tar.gz.age') != header['sha256']:
            raise ValueError('Stored archive hash mismatch')
        return dict(header, **{'verifiedAtUtc': receipt['verifiedAtUtc'], 'verified': True,
                              'manifestSha256': receipt['manifestSha256'], 'checksumPass': verify})

    def status(self, verify=False):
        folders = sorted(p for p in self.snapshots.iterdir() if STAMP.fullmatch(p.name))
        if not folders:
            raise ValueError('No completed database backup')
        latest = self.inspect(folders[-1], verify)
        age = (utc_now() - timestamp(latest['createdAtUtc'])).total_seconds()
        return {'healthy': -300 <= age <= self.config['maxAgeHours'] * 3600,
                'latest': latest, 'ageSeconds': age, 'snapshotCount': len(folders),
                'keepDaily': self.config['keepDaily'], 'maxAgeHours': self.config['maxAgeHours'],
                'maintenance': json.loads((self.root / 'maintenance.json').read_text()) if (self.root / 'maintenance.json').exists() else None}

    @staticmethod
    def remove(folder, parent, marker):
        canonical(folder)
        if folder.parent != parent or (folder / marker).read_text().strip() != MARKER:
            raise ValueError('Refusing deletion outside marked backup storage')
        for item in folder.rglob('*'):
            canonical(item)
        shutil.rmtree(folder)

    def maintain(self):
        started = utc_now().isoformat()
        try:
            latest = self.status(verify=True)
            if not latest['healthy']:
                raise ValueError('Latest backup is stale; retention held')
            folders = sorted(self.snapshots.iterdir(), reverse=True)
            selected_days = set()
            keep = set()
            for folder in folders:
                if not STAMP.fullmatch(folder.name):
                    continue
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
                      'verifiedStamp': latest['latest']['stamp'], 'removed': removed, 'retainedDaily': len(selected_days)}
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
