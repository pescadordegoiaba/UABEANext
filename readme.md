## UABEANext

UABEA with dock support. When this repo becomes stable and reaches feature parity, the code will be merged into the original UABEA repo.

Nightly:

- https://nightly.link/nesrak1/UABEANext/workflows/build-windows/master/uabea-windows.zip
- https://nightly.link/nesrak1/UABEANext/workflows/build-ubuntu/master/uabea-linux-x64.zip
- https://nightly.link/nesrak1/UABEANext/workflows/build-ubuntu/master/uabea-linux-musl-x64.zip

Linux packages are self-contained and do not require dotnet to be installed at
runtime. Use `linux-x64` first on Manjaro/Arch; use `linux-musl-x64` only as a
fallback for glibc compatibility problems. See `docs/linux-packaging.md`.

Report any issues on the original repo: https://github.com/nesrak1/UABEA
