#!/bin/bash

Arch="$1"
OutputPath="$2"

OutputArch="v2rayN-${Arch}"
FileName="v2rayN-${Arch}.zip"
URL="https://github.com/2dust/v2rayN-core-bin/raw/refs/heads/master/$FileName"

# Try to download, skip this architecture if failed
if command -v wget &> /dev/null; then
    wget -nv -O $FileName "$URL" || { echo "ERROR: Failed to download $FileName, skipping..."; exit 1; }
elif command -v curl &> /dev/null; then
    curl -fsSL -o $FileName "$URL" || { echo "ERROR: Failed to download $FileName, skipping..."; exit 1; }
else
    echo "ERROR: Neither wget nor curl is available"; exit 1
fi

ZipPath64="./$OutputArch"
mkdir $ZipPath64

cp -rf $OutputPath "$ZipPath64/$OutputArch"
7z a -tZip $FileName "$ZipPath64/$OutputArch" -mx1