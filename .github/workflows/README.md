# GitHub Actions Workflows

## Overview

FluxIndex uses two separate GitHub Actions workflows for CI/CD:

### 1. CI Build & Test (`ci.yml`)
- **Trigger**: All code changes (push/PR) except documentation
- **Purpose**: Continuous Integration - build and test on every change
- **Actions**:
  - Build the solution
  - Run all tests
  - Upload test results as artifacts

### 2. Release & Publish (`release.yml`)
- **Trigger**: Only when `Directory.Build.props` is modified on main branch
- **Purpose**: Release management and NuGet publishing
- **Actions**:
  - Build and test
  - Create NuGet packages
  - Publish to NuGet.org
  - Create GitHub Release

## Version Management

Version is centrally managed in `Directory.Build.props`:

```xml
<Version>0.1.0-preview</Version>
```

To release a new version:
1. Update the `<Version>` tag in `Directory.Build.props`
2. Commit and push to main branch
3. The release workflow will automatically:
   - Build and pack the NuGet package
   - Publish to NuGet.org
   - Create a GitHub Release with the new version tag

## Manual Release

You can also trigger a release manually using the workflow dispatch:
1. Go to Actions tab
2. Select "Release & Publish to NuGet"
3. Click "Run workflow"
4. Set "Publish to NuGet" to "true"

## Environment Secrets

The following secrets need to be configured in GitHub repository settings:
- `NUGET_API_KEY`: API key for publishing to NuGet.org

## Package Structure

- **Main Package**: `FluxIndex` - Contains all core functionality
- **Future Extensions** (planned):
  - `FluxIndex.AI.*` - AI provider integrations
  - `FluxIndex.Storage.*` - Storage provider integrations
  - `FluxIndex.Extensions.*` - Additional extensions