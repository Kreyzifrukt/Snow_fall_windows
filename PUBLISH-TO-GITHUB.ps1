$ErrorActionPreference = 'Stop'

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) is not installed. Install it from https://cli.github.com/ and rerun this script.'
}

if (-not (Test-Path '.git')) {
    throw 'The local Git history is missing. Extract the complete project folder and run this script from its root.'
}

gh auth status
if ($LASTEXITCODE -ne 0) {
    throw 'GitHub CLI is not authenticated. Run: gh auth login'
}

$repository = 'Kreyzifrukt/Snow_fall_windows'
$repositoryUrl = "https://github.com/$repository.git"
$knownImportCommit = '85bce5de5246a04586d65ab1414e721ee0a427d1'

$origin = git remote get-url origin 2>$null
if ($origin) {
    if ($origin -ne $repositoryUrl) {
        throw "The existing origin points to '$origin', not '$repositoryUrl'. Update it manually before publishing."
    }
}
else {
    git remote add origin $repositoryUrl
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not configure the GitHub remote.'
    }
}

git fetch origin main --tags
if ($LASTEXITCODE -ne 0) {
    throw 'Could not fetch the target branch. Check repository access and authentication.'
}

$remoteHead = (git rev-parse refs/remotes/origin/main).Trim()
if (-not $remoteHead) {
    throw 'The remote main branch could not be resolved.'
}

$comparisonStatus = gh api "repos/$repository/compare/$knownImportCommit...$remoteHead" --jq '.status'
if ($LASTEXITCODE -ne 0 -or $comparisonStatus -notin @('ahead', 'identical')) {
    throw 'The remote main branch is not descended from the expected initial import. Refusing to replace it; inspect the remote branch manually.'
}

Write-Host "The remote branch contains the temporary source-import history for $repository."
Write-Host 'This operation will replace that branch history with the complete local Git history and push the local tags.'
$confirmation = Read-Host 'Type REPLACE to continue'
if ($confirmation -cne 'REPLACE') {
    throw 'Publishing cancelled. The remote repository was not changed.'
}

git push "--force-with-lease=refs/heads/main:$remoteHead" --set-upstream origin main
if ($LASTEXITCODE -ne 0) {
    throw 'Could not replace the remote main branch. The remote may have changed since it was fetched.'
}

git push origin --tags
if ($LASTEXITCODE -ne 0) {
    throw 'The main branch was pushed, but one or more local tags could not be pushed.'
}

Write-Host "Published complete local history to https://github.com/$repository"
Write-Host 'Pushed the main branch and all local version tags, including v1.0.'
