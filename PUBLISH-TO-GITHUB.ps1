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

gh repo view $repository *> $null
if ($LASTEXITCODE -ne 0) {
    throw "The target repository https://github.com/$repository was not found or is not accessible to the authenticated account."
}

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

git push --set-upstream origin main
if ($LASTEXITCODE -ne 0) {
    throw 'Could not push the main branch. Check whether the remote branch contains commits that are not present locally.'
}

git push origin --tags
if ($LASTEXITCODE -ne 0) {
    throw 'The branch was pushed, but one or more version tags could not be pushed.'
}

Write-Host "Published https://github.com/$repository"
Write-Host 'Pushed the main branch and all local version tags, including v1.0.'
