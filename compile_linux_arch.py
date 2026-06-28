#!/usr/bin/env python3
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent

PACMAN_PACKAGES = [
    "base-devel",
    "git",
    "dotnet-sdk",
    "fontconfig",
    "freetype2",
    "libx11",
    "libxext",
    "libxrandr",
    "libxrender",
    "libxcursor",
    "libxi",
    "libxinerama",
    "libglvnd",
    "mesa",
    "gtk3",
    "xdg-desktop-portal",
    "zstd",
]


def run(cmd: list[str | Path], *, env: dict[str, str] | None = None) -> None:
    printable = " ".join(str(part) for part in cmd)
    print(f"+ {printable}", flush=True)
    subprocess.run([str(part) for part in cmd], cwd=ROOT, env=env, check=True)


def command_exists(name: str) -> bool:
    return shutil.which(name) is not None


def find_dotnet() -> Path | None:
    dotnet = shutil.which("dotnet")
    if dotnet:
        return Path(dotnet)
    home_dotnet = Path.home() / ".dotnet" / "dotnet"
    if home_dotnet.exists():
        return home_dotnet
    return None


def build_env(dotnet: Path) -> dict[str, str]:
    env = os.environ.copy()
    dotnet_dir = str(dotnet.parent)
    path = env.get("PATH", "")
    if dotnet_dir not in path.split(os.pathsep):
        env["PATH"] = dotnet_dir + os.pathsep + path
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["DOTNET_NOLOGO"] = "1"
    return env


def pacman_has_package(package: str) -> bool:
    return subprocess.run(
        ["pacman", "-Qi", package],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    ).returncode == 0


def install_packages(packages: list[str]) -> None:
    if not command_exists("pacman"):
        raise SystemExit("pacman nao encontrado. Este script de instalacao e focado em Arch/Manjaro.")

    missing = [package for package in packages if not pacman_has_package(package)]
    if not missing:
        print("Dependencias pacman ja instaladas.")
        return

    sudo_prefix: list[str] = []
    if os.geteuid() != 0:
        if not command_exists("sudo"):
            raise SystemExit("sudo nao encontrado. Instale as dependencias manualmente ou execute como root.")
        sudo_prefix = ["sudo"]

    run(sudo_prefix + ["pacman", "-S", "--needed", "--noconfirm", *missing])


def validate_common_requirements(rid: str, build_arch_package: bool) -> Path:
    dotnet = find_dotnet()
    if dotnet is None:
        raise SystemExit("dotnet SDK nao encontrado apos a instalacao das dependencias.")
    if not command_exists("bash"):
        raise SystemExit("bash nao encontrado.")
    if not command_exists("tar"):
        raise SystemExit("tar nao encontrado.")
    if build_arch_package and not command_exists("makepkg"):
        raise SystemExit("makepkg nao encontrado. Instale base-devel ou use --no-arch-package.")

    required_files = [
        ROOT / "UABEANext4.sln",
        ROOT / "ReleaseFiles" / "classdata.tpk",
        ROOT / "eng" / "linux" / "package-linux.sh",
    ]
    if build_arch_package:
        required_files.append(ROOT / "eng" / "linux" / "package-arch.sh")
    if rid == "linux-x64":
        required_files.extend(
            [
                ROOT / "NativeLibs" / "linux-x64" / "libtextureencoder.so",
                ROOT / "NativeLibs" / "linux-x64" / "libcuttlefish.so.2.10",
                ROOT / "NativeLibs" / "linux-x64" / "libPVRTexLib.so",
            ]
        )

    missing = [path for path in required_files if not path.exists()]
    if missing:
        formatted = "\n".join(f"- {path}" for path in missing)
        raise SystemExit(f"Arquivos obrigatorios ausentes:\n{formatted}")

    dotnet_version = subprocess.check_output([str(dotnet), "--version"], text=True).strip()
    print(f"dotnet SDK: {dotnet_version}")
    return dotnet


def warn_if_not_arch_like() -> None:
    if Path("/etc/manjaro-release").exists():
        print("Distribuicao detectada: Manjaro")
        return
    if Path("/etc/arch-release").exists():
        print("Distribuicao detectada: Arch/Arch-like")
        return
    print("Aviso: /etc/arch-release nao foi encontrado; continuando com validacoes genericas.")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Instala dependencias no Arch/Manjaro e empacota UABEANext para Linux.",
    )
    parser.add_argument("--rid", default="linux-x64", choices=["linux-x64", "linux-musl-x64"])
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--output", default=str(ROOT / "artifacts" / "linux"))
    parser.add_argument("--pkg-output", default=str(ROOT / "artifacts" / "pkgbuild"))
    parser.add_argument("--skip-install", action="store_true", help="Nao instala pacotes via pacman.")
    parser.add_argument("--no-arch-package", action="store_true", help="Nao gera pacote .pkg.tar.zst.")
    parser.add_argument("--check-only", action="store_true", help="Valida requisitos e sai sem compilar.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    warn_if_not_arch_like()

    build_arch_package = args.rid == "linux-x64" and not args.no_arch_package

    if not args.skip_install:
        install_packages(PACMAN_PACKAGES)
    else:
        print("Instalacao de dependencias ignorada por --skip-install.")

    dotnet = validate_common_requirements(args.rid, build_arch_package)
    env = build_env(dotnet)
    if args.check_only:
        print("Validacao concluida; build ignorado por --check-only.")
        return 0

    run(
        [
            "bash",
            ROOT / "eng" / "linux" / "package-linux.sh",
            "--rid",
            args.rid,
            "--configuration",
            args.configuration,
            "--output",
            args.output,
        ],
        env=env,
    )

    if build_arch_package:
        run(
            [
                "bash",
                ROOT / "eng" / "linux" / "package-arch.sh",
                "--tarball",
                Path(args.output) / f"uabea-next-{args.rid}.tar.gz",
                "--output",
                args.pkg_output,
            ],
            env=env,
        )

    print("Build Linux finalizado.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except subprocess.CalledProcessError as exc:
        raise SystemExit(exc.returncode) from exc
