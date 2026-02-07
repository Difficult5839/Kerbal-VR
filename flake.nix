{
  description = "KerbalVR development shell for NixOS";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-24.05";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs =
    { self, nixpkgs, flake-utils }:
    flake-utils.lib.eachDefaultSystem (
      system:
      let
        pkgs = import nixpkgs { inherit system; };
      in
      {
        devShells.default = pkgs.mkShell {
          packages = with pkgs; [
            bash
            coreutils
            findutils
            git
            gnugrep
            gnused
            msbuild
            mono
            nuget
            ripgrep
            rsync
            unzip
            zip
          ];

          shellHook = ''
            export NUGET_PACKAGES="$PWD/.nix-nuget-packages"
            export PATH="$PWD/scripts:$PATH"

            cat <<'EOF'
KerbalVR Nix dev shell ready.

Commands:
  kvr-build.sh <KSP_ROOT>
  kvr-package.sh <KSP_ROOT> [output-zip]
  kvr-install.sh <KSP_ROOT>

Example:
  kvr-build.sh "$HOME/.steam/steam/steamapps/common/Kerbal Space Program"
EOF
          '';
        };
      }
    );
}
