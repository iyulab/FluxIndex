#!/bin/bash

# Script to update package version
set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Parse arguments
VERSION=""
SUFFIX=""
COMMIT_CHANGES=false
CREATE_TAG=false

while [[ $# -gt 0 ]]; do
    case $1 in
        -v|--version)
            VERSION="$2"
            shift 2
            ;;
        -s|--suffix)
            SUFFIX="$2"
            shift 2
            ;;
        -c|--commit)
            COMMIT_CHANGES=true
            shift
            ;;
        -t|--tag)
            CREATE_TAG=true
            shift
            ;;
        -h|--help)
            echo "Usage: $0 -v VERSION [-s SUFFIX] [-c] [-t]"
            echo "  -v, --version    Version number (X.Y.Z format)"
            echo "  -s, --suffix     Version suffix (e.g., preview, beta)"
            echo "  -c, --commit     Commit changes to git"
            echo "  -t, --tag        Create git tag (requires -c)"
            exit 0
            ;;
        *)
            echo -e "${RED}Unknown option: $1${NC}"
            exit 1
            ;;
    esac
done

# Validate version
if [ -z "$VERSION" ]; then
    echo -e "${RED}Error: Version is required${NC}"
    echo "Usage: $0 -v VERSION [-s SUFFIX] [-c] [-t]"
    exit 1
fi

if ! [[ $VERSION =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo -e "${RED}Error: Invalid version format. Expected: X.Y.Z (e.g., 1.0.0)${NC}"
    exit 1
fi

# Build full version string
FULL_VERSION="$VERSION"
if [ -n "$SUFFIX" ]; then
    FULL_VERSION="$VERSION-$SUFFIX"
fi

ASSEMBLY_VERSION="$VERSION.0"
FILE_VERSION="$VERSION.0"

echo -e "${GREEN}Updating version to: $FULL_VERSION${NC}"

# Update Directory.Build.props
PROPS_FILE="src/Directory.Build.props"
if [ ! -f "$PROPS_FILE" ]; then
    echo -e "${RED}Error: Directory.Build.props not found at: $PROPS_FILE${NC}"
    exit 1
fi

# Update versions using sed (compatible with both Linux and macOS)
if [[ "$OSTYPE" == "darwin"* ]]; then
    # macOS
    sed -i '' "s|<Version>.*</Version>|<Version>$FULL_VERSION</Version>|g" "$PROPS_FILE"
    sed -i '' "s|<AssemblyVersion>.*</AssemblyVersion>|<AssemblyVersion>$ASSEMBLY_VERSION</AssemblyVersion>|g" "$PROPS_FILE"
    sed -i '' "s|<FileVersion>.*</FileVersion>|<FileVersion>$FILE_VERSION</FileVersion>|g" "$PROPS_FILE"
else
    # Linux
    sed -i "s|<Version>.*</Version>|<Version>$FULL_VERSION</Version>|g" "$PROPS_FILE"
    sed -i "s|<AssemblyVersion>.*</AssemblyVersion>|<AssemblyVersion>$ASSEMBLY_VERSION</AssemblyVersion>|g" "$PROPS_FILE"
    sed -i "s|<FileVersion>.*</FileVersion>|<FileVersion>$FILE_VERSION</FileVersion>|g" "$PROPS_FILE"
fi

echo -e "${GREEN}✅ Updated Directory.Build.props${NC}"

# Update root Directory.Build.props if it exists
ROOT_PROPS_FILE="Directory.Build.props"
if [ -f "$ROOT_PROPS_FILE" ]; then
    if [[ "$OSTYPE" == "darwin"* ]]; then
        sed -i '' "s|<Version>.*</Version>|<Version>$FULL_VERSION</Version>|g" "$ROOT_PROPS_FILE"
        sed -i '' "s|<AssemblyVersion>.*</AssemblyVersion>|<AssemblyVersion>$ASSEMBLY_VERSION</AssemblyVersion>|g" "$ROOT_PROPS_FILE"
        sed -i '' "s|<FileVersion>.*</FileVersion>|<FileVersion>$FILE_VERSION</FileVersion>|g" "$ROOT_PROPS_FILE"
    else
        sed -i "s|<Version>.*</Version>|<Version>$FULL_VERSION</Version>|g" "$ROOT_PROPS_FILE"
        sed -i "s|<AssemblyVersion>.*</AssemblyVersion>|<AssemblyVersion>$ASSEMBLY_VERSION</AssemblyVersion>|g" "$ROOT_PROPS_FILE"
        sed -i "s|<FileVersion>.*</FileVersion>|<FileVersion>$FILE_VERSION</FileVersion>|g" "$ROOT_PROPS_FILE"
    fi
    echo -e "${GREEN}✅ Updated root Directory.Build.props${NC}"
fi

# Show git diff
echo -e "${YELLOW}\nChanges made:${NC}"
git diff "$PROPS_FILE"

# Commit changes if requested
if [ "$COMMIT_CHANGES" = true ]; then
    echo -e "${YELLOW}\nCommitting changes...${NC}"
    git add "$PROPS_FILE"
    if [ -f "$ROOT_PROPS_FILE" ]; then
        git add "$ROOT_PROPS_FILE"
    fi
    
    COMMIT_MESSAGE="chore: bump version to $FULL_VERSION"
    git commit -m "$COMMIT_MESSAGE"
    echo -e "${GREEN}✅ Changes committed: $COMMIT_MESSAGE${NC}"
    
    # Create tag if requested
    if [ "$CREATE_TAG" = true ]; then
        TAG_NAME="v$FULL_VERSION"
        echo -e "${YELLOW}\nCreating tag: $TAG_NAME${NC}"
        git tag -a "$TAG_NAME" -m "Release version $FULL_VERSION"
        echo -e "${GREEN}✅ Tag created: $TAG_NAME${NC}"
        echo -e "${CYAN}Don't forget to push the tag: git push origin $TAG_NAME${NC}"
    fi
else
    echo -e "${YELLOW}\nChanges not committed. To commit, run with -c flag${NC}"
fi

echo -e "${GREEN}\n📦 Version update complete!${NC}"
echo -e "${CYAN}Next steps:${NC}"
echo "  1. Review the changes"
echo "  2. Run tests: dotnet test"
echo "  3. Build packages: dotnet pack"
if [ "$COMMIT_CHANGES" = false ]; then
    echo "  4. Commit changes: git commit -am 'chore: bump version to $FULL_VERSION'"
fi
echo "  5. Push to trigger CI/CD: git push"