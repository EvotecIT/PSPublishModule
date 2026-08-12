namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    /// <summary>Compiler, assembler, and linker options whose following token names filesystem input or state.</summary>
    private static readonly HashSet<string> BuildFlagPathOptions = new(StringComparer.Ordinal)
    {
        "--cuda-path", "--hip-path", "--hipspv-pass-plugin", "--hipstdpar-path",
        "--hipstdpar-prim-path", "--hipstdpar-thrust-path", "--libomptarget-amdgcn-bc-path",
        "--libomptarget-amdgpu-bc-path", "--libomptarget-nvptx-bc-path", "--libomptarget-spirv-bc-path",
        "--ptxas-path", "--rocm-device-lib-path", "--rocm-path", "--source-metadata-list",
        "--sysroot", "--warning-suppression-mappings",
        "-access-notes-path", "-api-diff-data-dir", "-api-diff-data-file", "-backup-module-interface-path",
        "-blocklist-file", "-candidate-module-file", "-cas-plugin-path", "-clang-build-session-file",
        "-clang-scanner-module-cache-path", "-const-gather-protocols-file", "-explicit-swift-module-map-file",
        "-external-pass-pipeline-filename", "-import-bridging-header", "-import-pch",
        "-in-process-plugin-server-path", "-internal-import-bridging-header", "-internal-import-pch",
        "-module-cache-path", "-new-driver-path", "-platform-availability-inheritance-map-path",
        "-prebuilt-module-cache-path", "-previous-module-installname-map-file", "-read-legacy-type-info-path",
        "-sdk-module-cache-path", "-tools-directory", "-verify-additional-file",
        "-B", "-F", "-I", "-L", "-MF", "-ccc-gcc-name", "-cxx-isystem", "-dependency-dot",
        "-dependency-file", "-dsym-dir", "-dumpdir", "-fapinotes-cache-path", "-fbuild-session-file",
        "-fcodegen-data-use", "-fexperimental-sanitize-metadata-ignorelist", "-fmemory-profile-use",
        "-fmodule-file", "-fmodule-map-file", "-fmodules-cache-path", "-fmodules-user-build-path",
        "-fms-secure-hotpatch-functions-file", "-fms-secure-hotpatch-functions-list", "-force_load", "-fpass-plugin",
        "-fplugin", "-fprebuilt-module-path", "-fprofile-instr-use", "-fprofile-instrument-use-path", "-fprofile-list",
        "-fprofile-remapping-file", "-fprofile-sample-use", "-fprofile-use",
        "-foverride-record-layout", "-frandomize-layout-seed-file", "-fsanitize-blacklist", "-fsanitize-coverage-allowlist",
        "-fsanitize-coverage-blacklist", "-fsanitize-coverage-ignorelist", "-fsanitize-coverage-whitelist",
        "-fsanitize-ignorelist", "-fsanitize-system-blacklist", "-fsanitize-system-ignorelist",
        "-fthinlto-distributor", "-fthinlto-index", "-fxray-always-instrument", "-fxray-attr-list", "-fxray-never-instrument",
        "-gcc-toolchain", "-gen-cdb-fragment-path",
        "-iapinotes-modules", "-iapinotes-path", "-idirafter", "-iframework", "-iframeworkwithsysroot",
        "-imacros", "-include", "-include-pch", "-include-pth", "-index-store-path",
        "-index-unit-output-path", "-install_name", "-iprefix", "-iquote", "-isysroot", "-isystem",
        "-isystem-after", "-ivfsstatcache", "-iwithprefix", "-iwithprefixbefore",
        "-iwithsysroot", "-ld-path", "-load", "-load-pass-plugin", "-load-plugin-library", "-module-file-info", "-module-map-file",
        "-multi-lib-config", "-plugin", "-plugin-path", "-profile-sample-use", "-profile-use",
        "-resource-dir", "-rpath", "-sdk", "-stdlib++-isystem", "-working-directory"
    };

    /// <summary>Joined forms of path-bearing compiler, assembler, and linker options, longest prefixes first.</summary>
    private static readonly string[] BuildFlagPathPrefixes =
    {
        "--libomptarget-amdgpu-bc-path=", "--libomptarget-amdgcn-bc-path=",
        "--libomptarget-nvptx-bc-path=", "--libomptarget-spirv-bc-path=",
        "--warning-suppression-mappings=", "--rocm-device-lib-path=", "--hipstdpar-thrust-path=",
        "--hipstdpar-prim-path=", "--hipspv-pass-plugin=", "--source-metadata-list=",
        "--hipstdpar-path=", "--cuda-path=", "--hip-path=", "--ptxas-path=", "--rocm-path=", "--sysroot=",
        "-platform-availability-inheritance-map-path=", "-previous-module-installname-map-file=",
        "-explicit-swift-module-map-file=", "-external-pass-pipeline-filename=",
        "-in-process-plugin-server-path=", "-clang-scanner-module-cache-path=",
        "-internal-import-bridging-header=", "-internal-import-pch=", "-clang-build-session-file=",
        "-import-bridging-header=", "-read-legacy-type-info-path=", "-const-gather-protocols-file=",
        "-verify-additional-file=", "-api-diff-data-dir=", "-api-diff-data-file=", "-cas-plugin-path=",
        "-tools-directory=", "-blocklist-file=", "-new-driver-path=", "-import-pch=", "-access-notes-path=",
        "-backup-module-interface-path=", "-prebuilt-module-cache-path=", "-candidate-module-file=",
        "-sdk-module-cache-path=", "-module-cache-path=",
        "-fexperimental-sanitize-metadata-ignorelist=", "-fms-secure-hotpatch-functions-file=",
        "-fms-secure-hotpatch-functions-list=", "-fsanitize-coverage-blacklist=",
        "-fsanitize-coverage-ignorelist=", "-fsanitize-coverage-whitelist=",
        "-fsanitize-coverage-allowlist=", "-foverride-record-layout=", "-frandomize-layout-seed-file=",
        "-fsanitize-system-blacklist=", "-fsanitize-system-ignorelist=",
        "-fprofile-remapping-file=", "-fmodules-user-build-path=", "-fprofile-instrument-use-path=", "-fprofile-instr-use=",
        "-fprofile-sample-use=", "-fapinotes-cache-path=", "-fbuild-session-file=",
        "-fcodegen-data-use=", "-fmemory-profile-use=", "-fmodules-cache-path=",
        "-fprebuilt-module-path=", "-fsanitize-ignorelist=", "-fsanitize-blacklist=",
        "-fthinlto-distributor=", "-fthinlto-index=", "-gen-cdb-fragment-path=", "-index-unit-output-path=",
        "-load-plugin-library=", "-load-pass-plugin=", "-fprofile-list=", "-fmodule-map-file=", "-fmodule-file=",
        "-fpass-plugin=", "-fprofile-use=", "-module-map-file=", "-profile-sample-use=",
        "-working-directory=", "-ccc-gcc-name=", "-dependency-file=", "-dependency-dot=",
        "-iapinotes-modules=", "-iapinotes-path=", "-index-store-path=", "-multi-lib-config=",
        "-object-file-name=", "-profile-use=", "-fxray-always-instrument=", "-fxray-attr-list=",
        "-fxray-never-instrument=", "-include-pch=",
        "-include-pth=", "-gcc-toolchain=", "-resource-dir=", "-ivfsstatcache=",
        "-plugin-path=", "-fplugin=", "-force_load=", "-ld-path=",
        "-stdlib++-isystem", "-iframeworkwithsysroot", "-iwithprefixbefore", "-cxx-isystem",
        "-iwithsysroot", "-iwithprefix", "-isystem-after", "-iframework", "-idirafter",
        "-imacros=", "-include=", "-imacros", "-include", "-isystem", "-iquote", "-iprefix", "-isysroot=",
        "-load=", "-plugin=", "-sdk=", "-MF", "-I", "-F", "-L", "-B"
    };
}
