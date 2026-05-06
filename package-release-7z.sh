#!/bin/bash

Arch="$1"
OutputPath="$2"

FileName="v2rayN-${Arch}.7z"

7z a -t7z -m0=lzma2 -mx=9 -mfb=64 -md=32m -ms=on $FileName "$OutputPath"