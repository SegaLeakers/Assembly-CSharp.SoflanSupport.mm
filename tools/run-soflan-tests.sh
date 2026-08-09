#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_MONO="${SOFLAN_UNITY_MONO:-}"
UNITY_EDITOR="${UNITY_EDITOR_PATH:-}"

find_unity_mono() {
    if [[ -n "$UNITY_MONO" && -x "$UNITY_MONO" ]]; then
        return 0
    fi

    if [[ -n "$UNITY_EDITOR" ]]; then
        for candidate in \
            "$UNITY_EDITOR/Contents/Resources/Scripting/MonoBleedingEdge/bin/mono" \
            "$UNITY_EDITOR/Contents/MonoBleedingEdge/bin/mono"; do
            if [[ -x "$candidate" ]]; then
                UNITY_MONO="$candidate"
                return 0
            fi
        done
    fi

    for candidate in \
        /Applications/Unity/Unity-*/Unity.app/Contents/Resources/Scripting/MonoBleedingEdge/bin/mono \
        /Applications/Unity/Hub/Editor/*/Unity.app/Contents/Resources/Scripting/MonoBleedingEdge/bin/mono \
        "$HOME"/Unity/Hub/Editor/*/Editor/Data/MonoBleedingEdge/bin/mono; do
        if [[ -x "$candidate" ]]; then
            UNITY_MONO="$candidate"
            return 0
        fi
    done

    return 1
}

build_net472() {
    local project="$1"
    dotnet build "$project" \
        -c Release \
        -f net472 \
        /p:FrameworkPathOverride="$UNITY_FRAMEWORK_PATH"
}

cd "$ROOT_DIR"

if find_unity_mono; then
    UNITY_MONO_ROOT="$(cd "$(dirname "$UNITY_MONO")/.." && pwd)"
    UNITY_FRAMEWORK_PATH="$UNITY_MONO_ROOT/lib/mono/4.7.2-api"
    UNITY_EDITOR_RESOLVED="${UNITY_MONO%%/Contents/*}"
    if [[ ! -d "$UNITY_FRAMEWORK_PATH" ]]; then
        echo "Unity Mono found, but 4.7.2-api is missing: $UNITY_FRAMEWORK_PATH" >&2
        exit 1
    fi

    echo "runtime=unity-mono"
    echo "UnityMono=$UNITY_MONO"
    echo "UnityEditorPath=$UNITY_EDITOR_RESOLVED"
    "$UNITY_MONO" --version | sed -n '1p'

    build_net472 tools/SoflanPrecisionTests/SoflanPrecisionTests.csproj
    "$UNITY_MONO" \
        tools/SoflanPrecisionTests/bin/Release/net472/SoflanPrecisionTests.exe \
        unity-mono

    build_net472 tools/SoflanVisibilityTests/SoflanVisibilityTests.csproj
    "$UNITY_MONO" \
        tools/SoflanVisibilityTests/bin/Release/net472/SoflanVisibilityTests.exe \
        unity-mono

    dotnet run \
        --project tools/SoflanVisibilityTests/SoflanVisibilityTests.csproj \
        -c Release \
        -f net8.0 \
        -- coreclr-secondary

    build_net472 tools/SoflanMaiBugTests/SoflanMaiBugTests.csproj
    MONO_PATH="tools/SoflanMaiBugTests/bin/Release/net472:Dependencies" \
        "$UNITY_MONO" \
        tools/SoflanMaiBugTests/bin/Release/net472/SoflanMaiBugTests.exe

    if [[ -n "${SOFLAN_INTEGRATION_CHART:-}" ]]; then
        build_net472 tools/SoflanRuntimeIntegrationTests/SoflanRuntimeIntegrationTests.csproj
        shared_internals="$UNITY_EDITOR_RESOLVED/Contents/Resources/Scripting/Managed/UnityEngine/UnityEngine.SharedInternalsModule.dll"
        if [[ ! -f "$shared_internals" ]]; then
            echo "UnityEngine.SharedInternalsModule.dll not found: $shared_internals" >&2
            exit 1
        fi
        cp "$shared_internals" \
            tools/SoflanRuntimeIntegrationTests/bin/Release/net472/UnityEngine.SharedInternalsModule.dll
        MONO_PATH="tools/SoflanRuntimeIntegrationTests/bin/Release/net472:Dependencies" \
            "$UNITY_MONO" \
            tools/SoflanRuntimeIntegrationTests/bin/Release/net472/SoflanRuntimeIntegrationTests.exe \
            "$SOFLAN_INTEGRATION_CHART"
    else
        echo "SoflanRuntimeIntegrationTests: SKIPPED (set SOFLAN_INTEGRATION_CHART)"
    fi

    if [[ -n "${SOFLAN_PATCHED_ASSEMBLY:-}" ]]; then
        build_net472 tools/SoflanClockTests/SoflanClockTests.csproj
        MONO_PATH="$(dirname "$SOFLAN_PATCHED_ASSEMBLY"):Dependencies" \
            "$UNITY_MONO" \
            tools/SoflanClockTests/bin/Release/net472/SoflanClockTests.exe \
            "$SOFLAN_PATCHED_ASSEMBLY"
    else
        echo "SoflanClockTests: SKIPPED (set SOFLAN_PATCHED_ASSEMBLY)"
    fi
else
    echo "runtime=coreclr fallback=true"
    dotnet run \
        --project tools/SoflanPrecisionTests/SoflanPrecisionTests.csproj \
        -c Release \
        -f net8.0 \
        -- coreclr
    dotnet run \
        --project tools/SoflanVisibilityTests/SoflanVisibilityTests.csproj \
        -c Release \
        -f net8.0 \
        -- coreclr
    echo "SoflanMaiBugTests: SKIPPED (requires Unity Mono/net472)"
    echo "SoflanRuntimeIntegrationTests: SKIPPED (requires Unity Mono)"
    echo "SoflanClockTests: SKIPPED (requires Unity Mono and patched Assembly-CSharp)"
fi
