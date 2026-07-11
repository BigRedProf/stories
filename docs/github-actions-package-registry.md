# GitHub Actions Package Registry Token

The GitHub Actions workflow restores and publishes BigRedProf packages through
GitHub Packages.

The NuGet source is configured in `.github/workflows/dotnet.yml` with this
secret:

```yaml
secrets.BIGREDPROF_GITHUB_PAT_PACKAGE_REGISTRY
```

If restore fails with a GitHub Packages `401 Unauthorized`, check this secret in
the repository or organization settings:

- Repository: `Settings` -> `Secrets and variables` -> `Actions`
- Organization: `Settings` -> `Secrets and variables` -> `Actions`

The token is a GitHub personal access token created from the owning user account:

`Settings` -> `Developer settings` -> `Personal access tokens`

The token must be able to read packages used during restore. It must also be able
to write packages when publishing from the workflow.

For a classic PAT, verify these scopes:

- `read:packages`
- `write:packages`

For a fine-grained PAT, verify it has access to the relevant BigRedProf
repositories/packages and package read/write permissions.

Common causes of restore failures:

- The secret is missing or blank.
- The PAT expired.
- The PAT owner no longer has package access.
- The PAT can publish packages but cannot read the package being restored.
- The package is owned by or linked to another repository the PAT cannot access.
