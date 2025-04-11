#!/bin/bash

Arch="$1"
OutputPath="$2"

OutputArch="v2rayN-${Arch}"
FileName="v2rayN-${Arch}.7z"

wget -nv -O $FileName "https://github.com/2dust/v2rayN-core-bin/raw/refs/heads/master/$FileName"

ZipPath64="./$OutputArch"
mkdir $ZipPath64

cp -rf $OutputPath "$ZipPath64/$OutputArch"
7z a -t7z -m0=lzma2 -mx=9 -mfb=64 -md=32m -ms=on $FileName "$ZipPath64/$OutputArch"