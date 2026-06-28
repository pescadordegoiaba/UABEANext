#!/usr/bin/env python3
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import urllib.request
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parent
DEFAULT_RID = "win-x64"
WINDOWS_NATIVE_FILES = ["textureencoder.dll", "cuttlefish.dll", "PVRTexLib.dll"]
PLUGIN_PROJECTS = {
    "AudioPlugin": [
        "AudioPlugin.dll",
        "Fmod5Sharp.dll",
        "NAudio.Core.dll",
        "OggVorbisEncoder.dll",
    ],
    "FontPlugin": ["FontPlugin.dll"],
    "MaterialPlugin": ["MaterialPlugin.dll"],
    "MeshPlugin": ["MeshPlugin.dll"],
    "TextAssetPlugin": ["TextAssetPlugin.dll"],
    "TexturePlugin": ["TexturePlugin.dll"],
    "UnityComponentPlugin": ["UnityComponentPlugin.dll"],
}
LINUX_ENV_VARS_TO_CLEAR = [
    "PKG_CONFIG_PATH",
    "PKG_CONFIG_LIBDIR",
    "PKG_CONFIG_SYSROOT_DIR",
    "LD_LIBRARY_PATH",
    "CMAKE_PREFIX_PATH",
    "QT_PLUGIN_PATH",
    "QML2_IMPORT_PATH",
]


def run(cmd: list[str | Path], *, env: dict[str, str] | None = None) -> None:
    printable = " ".join(str(part) for part in cmd)
    print(f"+ {printable}", flush=True)
    subprocess.run([str(part) for part in cmd], cwd=ROOT, env=env, check=True)


def command_exists(name: str) -> bool:
    return shutil.which(name) is not None


def pacman_has_package(package: str) -> bool:
    return subprocess.run(
        ["pacman", "-Qi", package],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    ).returncode == 0


def install_linux_cross_requirements(skip_install: bool) -> None:
    if skip_install:
        print("Instalacao de dependencias ignorada por --skip-install.")
        return
    if sys.platform.startswith("win"):
        return
    if not command_exists("pacman"):
        print("pacman nao encontrado; assumindo que as dependencias ja foram instaladas manualmente.")
        return

    packages = ["git", "dotnet-sdk"]
    missing = [package for package in packages if not pacman_has_package(package)]
    if not missing:
        print("Dependencias de cross-build Windows ja instaladas.")
        return

    sudo_prefix: list[str] = []
    if os.geteuid() != 0:
        if not command_exists("sudo"):
            raise SystemExit("sudo nao encontrado. Instale git e dotnet-sdk manualmente.")
        sudo_prefix = ["sudo"]

    run(sudo_prefix + ["pacman", "-S", "--needed", "--noconfirm", *missing])


def ensure_windows_dotnet(skip_install: bool) -> Path:
    dotnet = shutil.which("dotnet")
    if dotnet:
        return Path(dotnet)
    home_dotnet = Path.home() / ".dotnet" / ("dotnet.exe" if sys.platform.startswith("win") else "dotnet")
    if home_dotnet.exists():
        return home_dotnet
    if not sys.platform.startswith("win") or skip_install:
        raise SystemExit("dotnet SDK nao encontrado.")

    tools_dir = ROOT / ".build-tools" / "dotnet"
    tools_dir.mkdir(parents=True, exist_ok=True)
    installer = ROOT / ".build-tools" / "dotnet-install.ps1"
    if not installer.exists():
        print("Baixando dotnet-install.ps1...")
        urllib.request.urlretrieve("https://dot.net/v1/dotnet-install.ps1", installer)

    powershell = shutil.which("powershell") or shutil.which("pwsh")
    if not powershell:
        raise SystemExit("PowerShell nao encontrado para instalar o .NET SDK no Windows.")

    run(
        [
            powershell,
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            installer,
            "-Channel",
            "8.0",
            "-InstallDir",
            tools_dir,
        ]
    )
    dotnet = tools_dir / ("dotnet.exe" if sys.platform.startswith("win") else "dotnet")
    if not dotnet.exists():
        raise SystemExit("Falha ao instalar dotnet SDK.")
    return dotnet


def clean_windows_build_env(dotnet: Path) -> dict[str, str]:
    env = os.environ.copy()
    for var in LINUX_ENV_VARS_TO_CLEAR:
        env.pop(var, None)
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["DOTNET_NOLOGO"] = "1"
    dotnet_dir = str(dotnet.parent)
    path = env.get("PATH", "")
    if dotnet_dir and dotnet_dir not in path.split(os.pathsep):
        env["PATH"] = dotnet_dir + os.pathsep + path
    return env


def validate_requirements(dotnet: Path, rid: str) -> None:
    if rid != DEFAULT_RID:
        raise SystemExit("Somente win-x64 esta configurado neste projeto.")

    required_files = [
        ROOT / "UABEANext4.sln",
        ROOT / "UABEANext4.Desktop" / "UABEANext4.Desktop.csproj",
        ROOT / "ReleaseFiles" / "classdata.tpk",
    ]
    required_files.extend(ROOT / "NativeLibs" / rid / name for name in WINDOWS_NATIVE_FILES)
    for plugin in PLUGIN_PROJECTS:
        required_files.append(ROOT / plugin / f"{plugin}.csproj")

    missing = [path for path in required_files if not path.exists()]
    if missing:
        formatted = "\n".join(f"- {path}" for path in missing)
        raise SystemExit(f"Arquivos obrigatorios ausentes:\n{formatted}")

    dotnet_version = subprocess.check_output([str(dotnet), "--version"], text=True).strip()
    print(f"dotnet SDK: {dotnet_version}")


def copy_tree_contents(src: Path, dest: Path) -> None:
    if not src.exists():
        return
    dest.mkdir(parents=True, exist_ok=True)
    for item in src.iterdir():
        target = dest / item.name
        if item.is_dir():
            if target.exists():
                shutil.rmtree(target)
            shutil.copytree(item, target)
        else:
            shutil.copy2(item, target)


def copy_selected(src: Path, dest: Path, files: list[str]) -> None:
    dest.mkdir(parents=True, exist_ok=True)
    for file_name in files:
        source = src / file_name
        if source.exists():
            shutil.copy2(source, dest / file_name)


def zip_directory(source_dir: Path, zip_path: Path) -> None:
    if zip_path.exists():
        zip_path.unlink()
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as package:
        for path in sorted(source_dir.rglob("*")):
            if path.is_file():
                package.write(path, path.relative_to(source_dir.parent))


def build_windows_package(args: argparse.Namespace, dotnet: Path, env: dict[str, str]) -> None:
    output_root = Path(args.output).resolve()
    package_dir = output_root / f"uabea-next-{args.rid}"
    publish_dir = package_dir / "app"
    work_dir = ROOT / "artifacts" / "obj" / f"windows-{args.rid}"
    plugin_build_dir = work_dir / "plugins"

    if package_dir.exists():
        shutil.rmtree(package_dir)
    if work_dir.exists():
        shutil.rmtree(work_dir)
    publish_dir.mkdir(parents=True, exist_ok=True)
    plugin_build_dir.mkdir(parents=True, exist_ok=True)

    run([dotnet, "restore", ROOT / "UABEANext4.sln", "-r", args.rid], env=env)
    run(
        [
            dotnet,
            "publish",
            ROOT / "UABEANext4.Desktop" / "UABEANext4.Desktop.csproj",
            "-c",
            args.configuration,
            "-r",
            args.rid,
            "--self-contained",
            "true",
            "--no-restore",
            "-p:UseAppHost=true",
            "-p:PublishSingleFile=false",
            "-p:PublishReadyToRun=false",
            "-p:DebugType=None",
            "-p:DebugSymbols=false",
            "-o",
            publish_dir,
        ],
        env=env,
    )

    copy_tree_contents(ROOT / "ReleaseFiles", publish_dir)

    plugins_dir = publish_dir / "plugins"
    for plugin, files in PLUGIN_PROJECTS.items():
        plugin_out = plugin_build_dir / plugin
        print(f"Publishing plugin {plugin}...")
        run(
            [
                dotnet,
                "publish",
                ROOT / plugin / f"{plugin}.csproj",
                "-c",
                args.configuration,
                "-r",
                args.rid,
                "--self-contained",
                "false",
                "--no-restore",
                "-p:UABEACopyPluginToDesktop=false",
                "-p:DebugType=None",
                "-p:DebugSymbols=false",
                "-o",
                plugin_out,
            ],
            env=env,
        )
        copy_selected(plugin_out, plugins_dir, files)

    native_source = ROOT / "NativeLibs" / args.rid
    native_runtime_dir = publish_dir / "runtimes" / args.rid / "native"
    native_runtime_dir.mkdir(parents=True, exist_ok=True)
    for file_name in WINDOWS_NATIVE_FILES:
        source = native_source / file_name
        shutil.copy2(source, native_runtime_dir / file_name)
        shutil.copy2(source, publish_dir / file_name)

    exe_name = "UABEANext4.Desktop.exe"
    required_outputs = [
        publish_dir / exe_name,
        publish_dir / "classdata.tpk",
        plugins_dir / "TexturePlugin.dll",
        native_runtime_dir / "textureencoder.dll",
    ]
    missing_outputs = [path for path in required_outputs if not path.exists()]
    if missing_outputs:
        formatted = "\n".join(f"- {path}" for path in missing_outputs)
        raise SystemExit(f"Build Windows incompleto:\n{formatted}")

    readme = package_dir / "README-windows.txt"
    readme.write_text(
        "UABEANext Windows package (win-x64)\n\n"
        "Run:\n"
        f"  app\\{exe_name}\n\n"
        "This package is self-contained and targets Windows 10/11 x64.\n",
        encoding="utf-8",
    )

    if not args.no_zip:
        zip_path = output_root / f"uabea-next-{args.rid}.zip"
        zip_directory(package_dir, zip_path)
        print(f"Created {zip_path}")

    print(f"Windows package: {package_dir}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Baixa/valida requisitos e empacota UABEANext para Windows 10 x64.",
    )
    parser.add_argument("--rid", default=DEFAULT_RID, choices=[DEFAULT_RID])
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--output", default=str(ROOT / "artifacts" / "windows"))
    parser.add_argument("--skip-install", action="store_true", help="Nao instala/baixa dependencias.")
    parser.add_argument("--no-zip", action="store_true", help="Nao gera .zip final.")
    parser.add_argument("--check-only", action="store_true", help="Valida requisitos e sai sem compilar.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    install_linux_cross_requirements(args.skip_install)
    dotnet = ensure_windows_dotnet(args.skip_install)
    env = clean_windows_build_env(dotnet)
    validate_requirements(dotnet, args.rid)

    if args.check_only:
        print("Validacao concluida; build ignorado por --check-only.")
        return 0

    build_windows_package(args, dotnet, env)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except subprocess.CalledProcessError as exc:
        raise SystemExit(exc.returncode) from exc
