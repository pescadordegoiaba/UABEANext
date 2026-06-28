export PATH="$HOME/.dotnet:$PATH"
bash eng/linux/package-linux.sh --rid linux-x64
bash eng/linux/package-arch.sh
