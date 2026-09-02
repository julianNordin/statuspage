using 'main.bicep'

// Everything here comes from the environment. Nothing in this file is a value.
//
// The signing key especially: it is generated once per environment by the deploy script and
// lives in Key Vault afterwards. A parameter file with a real key in it is a secret in source
// control that looks like configuration, which is the most durable kind.

param administratorObjectId = readEnvironmentVariable('STATUSPAGE_ADMIN_OBJECT_ID')
param administratorName = readEnvironmentVariable('STATUSPAGE_ADMIN_NAME')
param jwtSigningKey = readEnvironmentVariable('STATUSPAGE_JWT_SIGNING_KEY')

// The operator password is the same shape of thing as the signing key: generated once by the
// deploy script, kept in Key Vault, never a value in this file. The address is not a secret,
// but it is environment-specific, so it comes from here too.
param operatorPassword = readEnvironmentVariable('STATUSPAGE_OPERATOR_PASSWORD')
param operatorEmail = readEnvironmentVariable('STATUSPAGE_OPERATOR_EMAIL', 'operator@statuspage.local')
param operatorDisplayName = readEnvironmentVariable('STATUSPAGE_OPERATOR_NAME', 'Operator')

// Images. Defaulted to GHCR under the repository that will exist at publish time; the deploy
// script overrides them when they are somewhere else.
param apiImage = readEnvironmentVariable('STATUSPAGE_API_IMAGE', 'ghcr.io/juliannordin/statuspage/api:latest')
param checkerImage = readEnvironmentVariable('STATUSPAGE_CHECKER_IMAGE', 'ghcr.io/juliannordin/statuspage/checker:latest')
param migrateImage = readEnvironmentVariable('STATUSPAGE_MIGRATE_IMAGE', 'ghcr.io/juliannordin/statuspage/migrate:latest')

// Empty means a public registry needing no credential. GHCR public packages are pullable
// anonymously, which is the whole reason there is no container registry in this deployment.
param registryServer = readEnvironmentVariable('STATUSPAGE_REGISTRY', '')

// Every ten minutes. Frequent enough that an outage is noticed promptly, and inside the
// Container Apps free grant with room to spare: 4,320 runs a month at roughly fifteen seconds
// and a quarter vCPU is about 16,000 vCPU-seconds against an allowance of 180,000.
param checkerCron = readEnvironmentVariable('STATUSPAGE_CHECKER_CRON', '*/10 * * * *')
