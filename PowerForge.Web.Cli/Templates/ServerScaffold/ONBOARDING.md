# PowerForge Linux Website Onboarding

The scaffold contains no private keys, API tokens, recovery identities, or decrypted backup material. Files ending in `.example` are deliberately non-operational.

## Repository

[ ] Review every generated file and keep the consumer workflow declarative.
[ ] Replace both authorized-key examples with real `.pub` values, preserve the `restrict` prefix, and remove the `.example` suffix.
__REPOSITORY_STEP__
[ ] Run `powerforge-web server plan --manifest deploy/linux/__SITE_ID__.serverrecovery.json`.

## GitHub

[ ] Create the `production` environment and restrict it to `__BRANCH__`.
[ ] Add variables `POWERFORGE_WEBSITE_DEPLOY_HOST=__HOST__`, `POWERFORGE_WEBSITE_DEPLOY_PORT=__SSH_PORT__`, and `POWERFORGE_WEBSITE_DEPLOY_USER=powerforge-__SITE_ID__`.
[ ] Add deployment secrets `DEPLOYMENT_SSH_PRIVATE_KEY` and `DEPLOYMENT_SSH_KNOWN_HOSTS`.
[ ] Add recovery secrets `SERVER_SSH_PRIVATE_KEY`, `SERVER_SSH_KNOWN_HOSTS`, `BACKUP_REPOSITORY_SSH_PRIVATE_KEY`, and `BACKUP_REPOSITORY_SSH_KNOWN_HOSTS`.
[ ] Keep deployment, server-capture, backup-repository, and private-source identities separate.

## Host

[ ] Generate bootstrap and secret-restore plans; review them before running as root.
[ ] Install the restricted deployment and backup accounts, strict sudoers files, Apache files, and pinned PowerForge runtime.
[ ] Obtain or restore the `__DOMAIN__` certificate and prove `certbot renew --dry-run`.
[ ] Run inspect and verify, then prove deployment, provenance, rollback, recovery, and scheduled refresh independently.

## Cloudflare

__CLOUDFLARE_STEP__
