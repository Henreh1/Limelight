using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Limelight.Services
{
    public sealed class LiveLoaderBridgeService
    {
        private const string BridgeName =
            "LimelightBridge";

        private const string BridgeScript =
     """
    local localAppData = os.getenv("LOCALAPPDATA")

    if localAppData == nil then
        print("[LimelightBridge] LOCALAPPDATA could not be found\n")
        return
    end

    local runtimeDirectory =
        localAppData .. "\\Limelight\\Runtime"

    local sessionBypassPath =
        runtimeDirectory .. "\\live-loader-disabled.txt"

    local sessionBypassFile =
        io.open(sessionBypassPath, "r")

    if sessionBypassFile ~= nil then
        local sessionBypassExpiry =
            tonumber(sessionBypassFile:read("*a"))

        sessionBypassFile:close()

        if sessionBypassExpiry ~= nil and
           sessionBypassExpiry >= os.time() then
            print("[LimelightBridge] Live Loader disabled for this session\n")
            return
        end

        os.remove(sessionBypassPath)
    end

    local heartbeatPath =
        runtimeDirectory .. "\\heartbeat.txt"

    local commandPath =
        runtimeDirectory .. "\\command.txt"

    local responsePath =
        runtimeDirectory .. "\\response.txt"

    local mountFunctionsPath =
        runtimeDirectory .. "\\mount-functions.txt"

    local lastHeartbeatSecond = 0
    local lastRequestId = nil
    local worldTransitioning = false
    local worldSettling = false
    local transitionGeneration = 0
    local automaticCharlieRefreshEnabled = false
    local activeCharliePortraitPath = nil
    local activeObjectPathsText = nil
    local portraitRefreshPassesRemaining = 0
    local lastPortraitRefreshSecond = 0

    local function writeHeartbeat()
        local heartbeatFile =
            io.open(heartbeatPath, "w")

        if heartbeatFile == nil then
            return
        end

        heartbeatFile:write(
            tostring(os.time()))

        heartbeatFile:close()
    end

    local function readValues(path)
        local file = io.open(path, "r")

        if file == nil then
            return nil
        end

        local values = {}

        for line in file:lines() do
            local key, value =
                line:match("^([^=]+)=(.*)$")

            if key ~= nil then
                values[key] = value
            end
        end

        file:close()
        return values
    end

    local function splitPipeSeparated(value)
        local values = {}

        if value == nil or value == "" then
            return values
        end

        for item in string.gmatch(value, "([^|]+)") do
            table.insert(values, item)
        end

        return values
    end

    local function writeResponse(
        requestId,
        success,
        message)

        local temporaryPath =
            responsePath .. ".tmp"

        local responseFile =
            io.open(temporaryPath, "w")

        if responseFile == nil then
            return
        end

        responseFile:write(
            "requestId=" .. tostring(requestId) .. "\n")

        responseFile:write(
            "success=" ..
            (success and "true" or "false") ..
            "\n")

        responseFile:write(
            "message=" .. tostring(message) .. "\n")

        responseFile:close()

        os.remove(responsePath)
        os.rename(
            temporaryPath,
            responsePath)
    end

    local function refreshCharliePortraitWidgets()
        if activeCharliePortraitPath == nil or
           activeCharliePortraitPath == "" then

            return 0
        end

        local portraitLoadSucceeded,
              activeCharliePortrait,
              portraitWasFound,
              portraitWasLoaded =
            pcall(function()
                return LoadAsset(
                    activeCharliePortraitPath)
            end)

        if not portraitLoadSucceeded or
           not portraitWasFound or
           not portraitWasLoaded or
           activeCharliePortrait == nil or
           not activeCharliePortrait:IsValid() then

            return 0
        end

        local activeNameReadSucceeded,
              activeFullName =
            pcall(function()
                return string.lower(
                    activeCharliePortrait:GetFullName())
            end)

        if not activeNameReadSucceeded then
            return 0
        end

        local imageWidgets =
            FindAllOf("Image")

        if imageWidgets == nil then
            return 0
        end

        local refreshedCount = 0

        for _, imageWidget in
            pairs(imageWidgets) do

            if imageWidget ~= nil and
               imageWidget:IsValid() then

                local resourceReadSucceeded,
                      resourceObject =
                    pcall(function()
                        local brush =
                            imageWidget.Brush

                        if brush == nil then
                            return nil
                        end

                        return brush.ResourceObject
                    end)

                if resourceReadSucceeded and
                   resourceObject ~= nil and
                   resourceObject:IsValid() then

                    local nameReadSucceeded,
                          resourceFullName =
                        pcall(function()
                            return string.lower(
                                resourceObject:GetFullName())
                        end)

                    local isCharliePortrait =
                        nameReadSucceeded and
                        string.find(
                            resourceFullName,
                            "dialog_charlie_01",
                            1,
                            true) ~= nil

                    if isCharliePortrait and
                       resourceFullName ~= activeFullName then

                        local setSucceeded =
                            pcall(function()
                                -- Keep the widget's existing layout size while
                                -- replacing only the texture behind its brush.
                                imageWidget:SetBrushFromTexture(
                                    activeCharliePortrait,
                                    false)
                            end)

                        if setSucceeded then
                            refreshedCount =
                                refreshedCount + 1
                        end
                    end
                end
            end
        end

        return refreshedCount
    end

    local function reloadAssets(
        objectPathsText,
        requireEveryAsset)
        local objectPaths =
            splitPipeSeparated(objectPathsText)

        if #objectPaths == 0 then
            return false,
                "The refresh command did not include any asset paths."
        end

        local loadedCount = 0
        local failures = {}

        for _, objectPath in ipairs(objectPaths) do
            local callSucceeded,
                  asset,
                  assetWasFound,
                  assetWasLoaded =
                pcall(function()
                    return LoadAsset(objectPath)
                end)

            if callSucceeded and
               assetWasFound and
               assetWasLoaded and
               asset ~= nil and
               asset:IsValid() then

                loadedCount = loadedCount + 1

                local lowerObjectPath =
                    string.lower(objectPath)

                if string.find(
                        lowerObjectPath,
                        "/ui/art/dialog/portraits/dialog_charlie_01.",
                        1,
                        true) ~= nil then

                    -- I save the path instead of keeping this UObject across
                    -- map loads. Unreal may retire the old package while the
                    -- Lua bridge is still alive, so a cached pointer can no
                    -- longer be trusted after a level change.
                    activeCharliePortraitPath = objectPath
                    portraitRefreshPassesRemaining = 20
    lastPortraitRefreshSecond = 0

                    local refreshedCount =
                        refreshCharliePortraitWidgets()

                    print(
                        "[LimelightBridge] Charlie portrait loaded; refreshed " ..
                        tostring(refreshedCount) ..
                        " existing image widget(s).\n")
                end
            else
                table.insert(
                    failures,
                    objectPath)
            end
        end

        if #failures > 0 and
           requireEveryAsset then

            return false,
                "The mounted character is still missing " ..
                tostring(#failures) ..
                " required material or texture package(s): " ..
                table.concat(failures, " | ")
        end

        if #failures > 0 then
            -- New textures and materials are often absent from the base Asset
            -- Registry. Unreal still loads them normally when SK_Charlie asks
            -- for its cooked dependencies from the mounted container.
            print(
                "[LimelightBridge] Preloaded " ..
                tostring(loadedCount) ..
                " registered assets. " ..
                tostring(#failures) ..
                " dependency packages will load through SK_Charlie.\n")

            return true,
                "Preloaded " .. tostring(loadedCount) ..
                " registered assets. " .. tostring(#failures) ..
                " dependency packages will load with the character."
        end

        return true,
            "Preloaded " .. tostring(loadedCount) ..
            " mounted assets, including interface and localization content."
    end

    local function scanCharlie()
        local playerControllers =
            FindAllOf("PlayerController")

        if playerControllers == nil then
            return false,
                "No player controllers were found. Enter a playable stage and try again."
        end

        local playerController = nil
        local pawn = nil

        -- Find the controller that currently owns a valid playable pawn.
        for _, candidateController in
            pairs(playerControllers) do

            if candidateController ~= nil and
               candidateController:IsValid() then

                local pawnReadSucceeded,
                      candidatePawn =
                    pcall(function()
                        return candidateController.Pawn
                    end)

                if pawnReadSucceeded and
                   candidatePawn ~= nil and
                   candidatePawn:IsValid() then

                    playerController =
                        candidateController

                    pawn =
                        candidatePawn

                    break
                end
            end
        end

        if playerController == nil or
           pawn == nil then

            return false,
                "No player controller currently owns a valid pawn."
        end

        local meshReadSucceeded, mesh =
            pcall(function()
                return pawn.Mesh
            end)

        if not meshReadSucceeded or
           mesh == nil or
           not mesh:IsValid() then

            return false,
                "The player pawn was found, but its Mesh component was unavailable."
        end

        local assetReadSucceeded, meshAsset =
            pcall(function()
                return mesh:GetSkeletalMeshAsset()
            end)

        if not assetReadSucceeded or
           meshAsset == nil or
           not meshAsset:IsValid() then

            return false,
                "The player mesh component was found, but its skeletal mesh asset was unavailable."
        end

        local assetName =
            meshAsset:GetFName():ToString()

        local message =
            "Pawn: " .. pawn:GetFullName() ..
            " | Component: " .. mesh:GetFullName() ..
            " | Asset: " .. meshAsset:GetFullName()

        if string.lower(assetName) ==
           "sk_charlie" then

            message =
                message ..
                " | SK_Charlie target confirmed"
        else
            message =
                message ..
                " | Expected SK_Charlie but found " ..
                assetName
        end

        return true, message
    end
    local function findActiveCharlieMeshComponent()
        local controllers =
            FindAllOf("PlayerController")

        if controllers == nil then
            return nil,
                "No active Charlie pawn is available yet."
        end

        for _, controller in pairs(controllers) do
            if controller ~= nil and
               controller:IsValid() then

                local pawnReadSucceeded,
                      pawn =
                    pcall(function()
                        return controller.Pawn
                    end)

                if pawnReadSucceeded and
                   pawn ~= nil and
                   pawn:IsValid() then

                    local pawnName =
                        string.lower(
                            pawn:GetFullName())

                    if string.find(
                           pawnName,
                           "bp_pagodaplayercharacter_charlie",
                           1,
                           true) ~= nil then

                        local meshReadSucceeded,
                              meshComponent =
                            pcall(function()
                                return pawn.Mesh
                            end)

                        if meshReadSucceeded and
                           meshComponent ~= nil and
                           meshComponent:IsValid() then

                            return meshComponent,
                                pawn:GetFullName()
                        end
                    end
                end
            end
        end

        return nil,
            "No active Charlie pawn is available yet."
    end

    local function inspectCharlieMaterials(
        meshComponent)

        local inspectionSucceeded,
              materialsReady,
              materialSummary =
            pcall(function()
                local materialCount =
                    meshComponent:GetNumMaterials()

                if materialCount == nil or
                   materialCount <= 0 then

                    return false,
                        "the replacement mesh has no material slots"
                end

                local validMaterialCount = 0
                local fallbackSlots = {}
                local activeMaterials = {}

                for materialIndex = 0,
                    materialCount - 1 do

                    local material =
                        meshComponent:GetMaterial(
                            materialIndex)

                    if material ~= nil and
                       material:IsValid() then

                        local fullName =
                            material:GetFullName()

                        local lowerName =
                            string.lower(fullName)

                        table.insert(
                            activeMaterials,
                            fullName)

                        local isFallbackMaterial =
                            string.find(
                                lowerName,
                                "worldgridmaterial",
                                1,
                                true) ~= nil or
                            string.find(
                                lowerName,
                                "defaultmaterial",
                                1,
                                true) ~= nil or
                            string.find(
                                lowerName,
                                "defaultsurfacematerial",
                                1,
                                true) ~= nil

                        if isFallbackMaterial then
                            table.insert(
                                fallbackSlots,
                                tostring(materialIndex))
                        else
                            validMaterialCount =
                                validMaterialCount + 1
                        end
                    else
                        table.insert(
                            activeMaterials,
                            "<empty slot " ..
                            tostring(materialIndex) ..
                            ">")
                    end
                end

                if #fallbackSlots > 0 then
                    return false,
                        "Unreal assigned a fallback material to slot(s) " ..
                        table.concat(fallbackSlots, ", ")
                end

                if validMaterialCount == 0 then
                    return false,
                        "the replacement mesh has no valid mod materials"
                end

                return true,
                    table.concat(
                        activeMaterials,
                        " | ")
            end)

        if not inspectionSucceeded then
            return false,
                "material inspection failed: " ..
                tostring(materialsReady)
        end

        return materialsReady,
            materialSummary
    end

    local function reapplyCharlie()
        local loadCallSucceeded,
              meshAsset,
              assetWasFound,
              assetWasLoaded =
            pcall(function()
                return LoadAsset(
                    "/Game/Pagoda/Characters/Player/Meshes/SK_Charlie.SK_Charlie")
            end)

        if not loadCallSucceeded then
            return false,
                "The replacement SK_Charlie asset could not be loaded: " ..
                tostring(meshAsset)
        end

        if not assetWasFound or
           not assetWasLoaded or
           meshAsset == nil or
           not meshAsset:IsValid() then

            return false,
                "The newly mounted container did not provide a loadable SK_Charlie asset."
        end

        local assetName =
            string.lower(
                meshAsset:GetFName():ToString())

        if assetName ~= "sk_charlie" then
            return false,
                "The freshly loaded asset was not SK_Charlie."
        end

        local meshComponent,
              pawnName =
            findActiveCharlieMeshComponent()

        if meshComponent == nil then
            return false,
                pawnName
        end

        local previousMesh = nil

        pcall(function()
            previousMesh =
                meshComponent.SkeletalMesh
        end)

        local clearedOverrideCount = 0

        local setSucceeded,
              setError =
            pcall(function()
                local overrideMaterials =
                    meshComponent.OverrideMaterials

                if overrideMaterials ~= nil then
                    clearedOverrideCount =
                        overrideMaterials:GetArrayNum()

                    -- CharacterMesh0 can keep dynamic overrides from the old
                    -- model. I clear them only on the live player component.
                    overrideMaterials:Empty()
                end

                meshComponent:SetSkeletalMeshAsset(
                    meshAsset)
            end)

        if not setSucceeded then
            return false,
                "The active Charlie pawn could not accept the replacement mesh: " ..
                tostring(setError)
        end

        local materialsReady,
              materialSummary =
            inspectCharlieMaterials(
                meshComponent)

        if not materialsReady then
            -- A black model is never a successful switch. I restore the old
            -- mesh and let Limelight retry after dependencies finish loading.
            if previousMesh ~= nil and
               previousMesh:IsValid() then

                pcall(function()
                    meshComponent:SetSkeletalMeshAsset(
                        previousMesh)
                end)
            end

            return false,
                "The replacement materials are not ready: " ..
                materialSummary
        end

        print(
            "[LimelightBridge] Cleared " ..
            tostring(clearedOverrideCount) ..
            " material overrides on the active Charlie pawn. Active materials: " ..
            materialSummary ..
            "\n")

        return true,
            "A fresh SK_Charlie asset and verified materials were applied to " ..
            pawnName .. "."
    end

    local function scanMountFunctions()
        local candidates = {}
        local candidateSet = {}
        local scannedObjectCount = 0

        -- Search reflected Unreal functions without printing every object.
        ForEachUObject(function(object)
            scannedObjectCount =
                scannedObjectCount + 1

            local nameReadSucceeded,
                  fullName =
                pcall(function()
                    return object:GetFullName()
                end)

            if nameReadSucceeded and
               fullName ~= nil then

                local lowerName =
                    string.lower(fullName)

                local isFunction =
                    string.sub(
                        lowerName,
                        1,
                        9) == "function "

                local mentionsMount =
                    string.find(
                        lowerName,
                        "mount",
                        1,
                        true) ~= nil

                local mentionsContainer =
                    string.find(
                        lowerName,
                        "pak",
                        1,
                        true) ~= nil or
                    string.find(
                        lowerName,
                        "iostore",
                        1,
                        true) ~= nil or
                    string.find(
                        lowerName,
                        "container",
                        1,
                        true) ~= nil or
                    string.find(
                        lowerName,
                        "chunk",
                        1,
                        true) ~= nil

                local mentionsPakAction =
                    string.find(
                        lowerName,
                        "loadpak",
                        1,
                        true) ~= nil or
                    string.find(
                        lowerName,
                        "openpak",
                        1,
                        true) ~= nil

                if isFunction and
                   ((mentionsMount and mentionsContainer) or
                    mentionsPakAction) and
                   candidateSet[fullName] == nil then

                    candidateSet[fullName] = true

                    table.insert(
                        candidates,
                        fullName)
                end
            end
        end)

        table.sort(candidates)

        local reportFile =
            io.open(
                mountFunctionsPath,
                "w")

        if reportFile == nil then
            return false,
                "Limelight could not create the mount-function report."
        end

        reportFile:write(
            "Objects scanned: " ..
            tostring(scannedObjectCount) ..
            "\n")

        reportFile:write(
            "Candidate functions: " ..
            tostring(#candidates) ..
            "\n\n")

        for _, candidate in
            ipairs(candidates) do

            reportFile:write(
                candidate .. "\n")
        end

        reportFile:close()

        if #candidates == 0 then
            return false,
                "No reflected mounting functions were found. The report was saved to " ..
                mountFunctionsPath
        end

        return true,
            tostring(#candidates) ..
            " possible mounting functions were found. The report was saved to " ..
            mountFunctionsPath
    end

    local function processCommand()
        local command =
            readValues(commandPath)

        if command == nil then
            return
        end

        local requestId =
            command.requestId

        if requestId == nil or
           requestId == "" then

            os.remove(commandPath)
            return
        end

        if requestId == lastRequestId then
            os.remove(commandPath)
            return
        end

        lastRequestId = requestId

        local action =
            string.lower(
                command.action or "")

        if action == "ping" then
            writeResponse(
                requestId,
                true,
                "Limelight bridge is online")
        elseif action == "scan_mount_functions" then
            ExecuteInGameThread(function()
                local callSucceeded,
                      scanSucceeded,
                      scanMessage =
                    pcall(scanMountFunctions)

                if not callSucceeded then
                    writeResponse(
                        requestId,
                        false,
                        "Mount-function scan failed: " ..
                        tostring(scanSucceeded))
                else
                    writeResponse(
                        requestId,
                        scanSucceeded,
                        scanMessage)
                end
            end)
        elseif action == "reapply_charlie" then
            ExecuteInGameThread(function()
                if worldTransitioning or worldSettling then
                    writeResponse(
                        requestId,
                        false,
                        "A level is still loading. Limelight will retry once the new world is ready.")

                    return
                end

                local callSucceeded,
                      reapplySucceeded,
                      reapplyMessage =
                    pcall(reapplyCharlie)

                if not callSucceeded then
                    writeResponse(
                        requestId,
                        false,
                        "Model reapply failed: " ..
                        tostring(reapplySucceeded))
                else
                    if reapplySucceeded then
                        automaticCharlieRefreshEnabled = true
                    end

                    writeResponse(
                        requestId,
                        reapplySucceeded,
                        reapplyMessage)
                end
            end)
        elseif action == "reload_assets" then
            ExecuteInGameThread(function()
                if worldTransitioning or worldSettling then
                    writeResponse(
                        requestId,
                        false,
                        "A level is still loading. Mounted assets were left untouched until it finishes.")

                    return
                end

                local callSucceeded,
                      reloadSucceeded,
                      reloadMessage =
                    pcall(function()
                        return reloadAssets(
                            command.objectPaths,
                            command.requireEveryAsset == "true")
                    end)

                if not callSucceeded then
                    writeResponse(
                        requestId,
                        false,
                        "Mounted asset reload failed: " ..
                        tostring(reloadSucceeded))
                else
                    if reloadSucceeded then
                        automaticCharlieRefreshEnabled = true

                        -- I keep the complete active asset list so a newly loaded world
                        -- can request fresh portrait, interface, and localization objects.
                        activeObjectPathsText =
                            command.objectPaths
                    end

                    writeResponse(
                        requestId,
                        reloadSucceeded,
                        reloadMessage)
                end
            end)
        else
            writeResponse(
                requestId,
                false,
                "Unknown bridge command: " .. action)
        end

        os.remove(commandPath)
    end

    local function scheduleAutomaticCharlieRefresh(
        delayMilliseconds,
        expectedGeneration)

        if not automaticCharlieRefreshEnabled then
            return
        end

        ExecuteInGameThreadWithDelay(
            delayMilliseconds,
            function()
                if worldTransitioning or
                   worldSettling or
                   expectedGeneration ~= transitionGeneration or
                   not automaticCharlieRefreshEnabled then

                    return
                end

                local assetReloadCallSucceeded = true
                local assetsReloaded = true
                local assetReloadMessage =
                    "No active asset paths were saved."

                if activeObjectPathsText ~= nil and
                   activeObjectPathsText ~= "" then

                    -- The old world may have released interface and localization
                    -- objects. I load the active packages again before touching
                    -- any of the newly created widgets.
                    assetReloadCallSucceeded,
                    assetsReloaded,
                    assetReloadMessage =
                        pcall(function()
                            return reloadAssets(
                                activeObjectPathsText,
                                false)
                        end)
                end

                local reapplyCallSucceeded,
                      refreshSucceeded,
                      refreshMessage =
                    pcall(reapplyCharlie)

                if assetReloadCallSucceeded and
                   assetsReloaded and
                   reapplyCallSucceeded and
                   refreshSucceeded then

                    print(
                        "[LimelightBridge] Automatic post-load refresh: " ..
                        tostring(assetReloadMessage) ..
                        " " ..
                        tostring(refreshMessage) ..
                        "\n")
                else
                    print(
                        "[LimelightBridge] Automatic post-load refresh is still waiting. Assets: " ..
                        tostring(assetReloadMessage) ..
                        " Character: " ..
                        tostring(refreshMessage) ..
                        "\n")
                end
            end)
    end

    RegisterLoadMapPreHook(function()
        worldTransitioning = true
        worldSettling = true
        transitionGeneration =
            transitionGeneration + 1

        print(
            "[LimelightBridge] Level transition started; model refresh paused.\n")
    end)

    RegisterLoadMapPostHook(function()
        worldTransitioning = false
        worldSettling = true

        local completedGeneration =
            transitionGeneration

        if activeCharliePortraitPath ~= nil and
           activeCharliePortraitPath ~= "" then

            -- A new map creates fresh widgets, so I give the portrait another
            -- chance to reach screens constructed after the transition. The
            -- next refresh resolves a new UObject from the saved path.
            portraitRefreshPassesRemaining = 20
            lastPortraitRefreshSecond = 0
        end

        -- LoadMap finishes before every streamed actor and widget is ready. I
        -- keep every refresh locked until the same quiet period used by the
        -- native bridge has passed without another map starting.
        ExecuteInGameThreadWithDelay(
            6000,
            function()
                if worldTransitioning or
                   completedGeneration ~= transitionGeneration then

                    return
                end

                worldSettling = false

                scheduleAutomaticCharlieRefresh(
                    0,
                    completedGeneration)

                print(
                    "[LimelightBridge] New level settled; model refresh unlocked.\n")
            end)

        print(
            "[LimelightBridge] Level transition finished; model refresh scheduled.\n")
    end)

    RegisterBeginPlayPostHook(function(contextParameter)
        if worldTransitioning or
           worldSettling or
           not automaticCharlieRefreshEnabled then

            return
        end

        local context = contextParameter:get()

        if context == nil or
           not context:IsValid() then

            return
        end

        local contextName =
            string.lower(
                context:GetFullName())

        if string.find(
                contextName,
                "bp_pagodaplayercharacter_charlie",
                1,
                true) ~= nil then

            -- This catches characters created after LoadMap's normal delay,
            -- including streamed stages and late player respawns.
            scheduleAutomaticCharlieRefresh(
                350,
                transitionGeneration)
        end
    end)

    -- Produce a heartbeat immediately so the dashboard can recognise us.
    writeHeartbeat()
    lastHeartbeatSecond = os.time()

    LoopAsync(250, function()
        local currentSecond =
            os.time()

        if currentSecond ~=
           lastHeartbeatSecond then

            writeHeartbeat()
            lastHeartbeatSecond =
                currentSecond
        end

            if portraitRefreshPassesRemaining > 0 and
       currentSecond ~= lastPortraitRefreshSecond and
       not worldTransitioning and
       not worldSettling then

        -- Portrait widgets are often created after the texture loads. I
        -- retry briefly so newly opened screens receive the active image.
        local refreshedCount =
            refreshCharliePortraitWidgets()

        portraitRefreshPassesRemaining =
            portraitRefreshPassesRemaining - 1

        lastPortraitRefreshSecond =
            currentSecond

        if refreshedCount > 0 then
            print(
                "[LimelightBridge] Refreshed " ..
                tostring(refreshedCount) ..
                " Charlie portrait widget(s).\n")
        end
    end

        processCommand()

        -- Returning false keeps the bridge loop running.
        return false
    end)

    print("[LimelightBridge] Runtime bridge online\n")
    """;

        public string RuntimeDirectory =>
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Limelight",
                "Runtime");

        public string HeartbeatPath =>
            Path.Combine(
                RuntimeDirectory,
                "heartbeat.txt");

        public string SessionBypassPath =>
            Path.Combine(
                RuntimeDirectory,
                "live-loader-disabled.txt");

        public void SetSessionBypass(
            bool isDisabled)
        {
            if (!isDisabled)
            {
                if (File.Exists(SessionBypassPath))
                {
                    File.Delete(SessionBypassPath);
                }

                return;
            }

            Directory.CreateDirectory(
                RuntimeDirectory);

            // I give the marker an expiry so an interrupted Limelight process
            // can never leave future game launches without the bridge.
            long expiry =
                DateTimeOffset.UtcNow
                    .AddMinutes(10)
                    .ToUnixTimeSeconds();

            File.WriteAllText(
                SessionBypassPath,
                expiry.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
        }

        public void EnsureInstalled(
            Ue4ssDetectionResult installation)
        {
            if (!installation.IsInstalled)
            {
                throw new InvalidOperationException(
                    "UE4SS must be installed before adding the Limelight bridge.");
            }

            if (string.IsNullOrWhiteSpace(
                    installation.ModsDirectory))
            {
                throw new DirectoryNotFoundException(
                    "The UE4SS Mods directory could not be determined.");
            }

            Directory.CreateDirectory(
                RuntimeDirectory);

            string scriptsDirectory =
                Path.Combine(
                    installation.ModsDirectory,
                    BridgeName,
                    "scripts");

            Directory.CreateDirectory(
                scriptsDirectory);

            string scriptPath =
                Path.Combine(
                    scriptsDirectory,
                    "main.lua");

            WriteScriptIfChanged(
                scriptPath);

            string modsTextPath =
                Path.Combine(
                    installation.ModsDirectory,
                    "mods.txt");

            EnableBridgeInModsFile(
                modsTextPath);
        }

        public bool IsInstalled(
            Ue4ssDetectionResult installation)
        {
            if (!installation.IsInstalled ||
                string.IsNullOrWhiteSpace(
                    installation.ModsDirectory))
            {
                return false;
            }

            string scriptPath =
                Path.Combine(
                    installation.ModsDirectory,
                    BridgeName,
                    "scripts",
                    "main.lua");

            string modsTextPath =
                Path.Combine(
                    installation.ModsDirectory,
                    "mods.txt");

            if (!File.Exists(scriptPath) ||
                !File.Exists(modsTextPath))
            {
                return false;
            }

            try
            {
                return File.ReadLines(modsTextPath)
                    .Any(IsEnabledBridgeLine);
            }
            catch
            {
                return false;
            }
        }

        public bool HasBridgeFiles(
            Ue4ssDetectionResult installation)
        {
            if (!installation.IsInstalled ||
                string.IsNullOrWhiteSpace(
                    installation.ModsDirectory))
            {
                return false;
            }

            string scriptPath =
                Path.Combine(
                    installation.ModsDirectory,
                    BridgeName,
                    "scripts",
                    "main.lua");

            // The bridge script only exists after the user has accepted
            // setup, so it is safe for Limelight to repair its mods.txt entry.
            return File.Exists(scriptPath);
        }

        public bool IsOnline()
        {
            try
            {
                if (!File.Exists(HeartbeatPath))
                {
                    return false;
                }

                DateTime lastHeartbeat =
                    File.GetLastWriteTimeUtc(
                        HeartbeatPath);

                TimeSpan heartbeatAge =
                    DateTime.UtcNow -
                    lastHeartbeat;

                // The bridge writes once per second. Five seconds leaves room
                // for a loading screen or a short frame-rate stall.
                return heartbeatAge >=
                           TimeSpan.FromSeconds(-2) &&
                       heartbeatAge <=
                           TimeSpan.FromSeconds(5);
            }
            catch
            {
                return false;
            }
        }

        public void ClearHeartbeat()
        {
            try
            {
                if (File.Exists(HeartbeatPath))
                {
                    File.Delete(HeartbeatPath);
                }
            }
            catch
            {
                // A stale heartbeat naturally expires after five seconds, so
                // failing to remove it is harmless.
            }
        }

        private static void WriteScriptIfChanged(
            string scriptPath)
        {
            if (File.Exists(scriptPath))
            {
                string existingScript =
                    File.ReadAllText(scriptPath);

                if (string.Equals(
                        existingScript,
                        BridgeScript,
                        StringComparison.Ordinal))
                {
                    return;
                }
            }

            // Limelight owns this one script, so updating it does not affect
            // any other UE4SS mods the user has installed.
            File.WriteAllText(
                scriptPath,
                BridgeScript);
        }

        private static void EnableBridgeInModsFile(
            string modsTextPath)
        {
            List<string> lines =
                File.Exists(modsTextPath)
                    ? File.ReadAllLines(modsTextPath).ToList()
                    : new List<string>();

            int existingLineIndex =
                lines.FindIndex(IsBridgeLine);

            if (existingLineIndex >= 0)
            {
                if (IsEnabledBridgeLine(
                        lines[existingLineIndex]))
                {
                    return;
                }

                lines[existingLineIndex] =
                    $"{BridgeName} : 1";
            }
            else
            {
                if (lines.Count > 0 &&
                    !string.IsNullOrWhiteSpace(lines[^1]))
                {
                    lines.Add(string.Empty);
                }

                lines.Add(
                    $"{BridgeName} : 1");
            }

            string? modsDirectory =
                Path.GetDirectoryName(modsTextPath);

            if (!string.IsNullOrWhiteSpace(modsDirectory))
            {
                Directory.CreateDirectory(
                    modsDirectory);
            }

            string temporaryPath =
                modsTextPath + ".limelight.tmp";

            try
            {
                File.WriteAllLines(
                    temporaryPath,
                    lines);

                if (File.Exists(modsTextPath))
                {
                    // Keep one small safety copy because mods.txt may also
                    // contain entries belonging to other tools.
                    File.Copy(
                        modsTextPath,
                        modsTextPath + ".limelight.bak",
                        overwrite: true);
                }

                File.Move(
                    temporaryPath,
                    modsTextPath,
                    overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static bool IsBridgeLine(
            string line)
        {
            string[] parts =
                line.Split(
                    ':',
                    count: 2);

            return parts.Length > 0 &&
                   string.Equals(
                       parts[0].Trim(),
                       BridgeName,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEnabledBridgeLine(
            string line)
        {
            string[] parts =
                line.Split(
                    ':',
                    count: 2);

            return parts.Length == 2 &&
                   string.Equals(
                       parts[0].Trim(),
                       BridgeName,
                       StringComparison.OrdinalIgnoreCase) &&
                   parts[1].Trim().StartsWith(
                       "1",
                       StringComparison.Ordinal);
        }
    }
}
