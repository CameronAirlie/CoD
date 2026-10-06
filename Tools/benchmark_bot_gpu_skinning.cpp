// Actual soldier/clip renderer benchmark; excludes gameplay/editor overhead.
#include "PlutoGE/assets/AssetManager.h"
#include "PlutoGE/render/Mesh.h"
#include "PlutoGE/render/Material.h"
#include "PlutoGE/render/RhiSceneRenderer.h"
#include "PlutoGE/render/ShaderArtifacts.h"
#include "PlutoGE/render/rhi/RenderDeviceFactory.h"
#include "PlutoGE/scene/components/AnimationComponent.h"
#include <array>
#include <iostream>
#include <fstream>
#include <stdexcept>
#include <glm/gtc/matrix_transform.hpp>

int main(int argc,char **argv) try
{
    using namespace PlutoGE;
    using namespace render;
    if (argc != 2) return 2;
    std::ofstream progress("gpu-skinning-benchmark-progress.log");
    progress << "Loading assets" << std::endl;
    assets::AssetManager assets; assets.SetProjectContext(argv[1]);
    auto *mesh = assets.LoadMeshAsset("project://Bots/Soldier/Soldier_Game.plutomesh");
    std::vector<AnimationClip> clips;
    if (!mesh || !assets.LoadAnimationAsset("project://Bots/Soldier/Soldier.plutoanim",clips))
        throw std::runtime_error("Missing bot assets");
    auto created = rhi::CreateRenderDevice(rhi::GraphicsApi::Vulkan);
    if (!created) throw std::runtime_error(created.error);
    auto &device = *created.device;
    progress << "Device " << created.deviceName << "; mesh " << mesh->GetVertexCount() << " vertices / " << mesh->GetSubmeshCount() << " sections" << std::endl;
    Material material({.color={.6f,.5f,.3f,1}});
    std::array<std::vector<glm::mat4>,9> palettes;
    std::vector<RenderCommand> commands;
    for (unsigned actor = 0; actor < palettes.size(); ++actor)
        for (unsigned submesh = 0; submesh < mesh->GetSubmeshCount(); ++submesh)
        {
            RenderCommand command; command.mesh = mesh; command.material = &material;
            command.jointMatrices = &palettes[actor]; command.submeshIndex = submesh;
            command.model = glm::translate(glm::mat4(1),glm::vec3(float(actor%3)*2-2,0,-float(actor/3)*2));
            command.previousModel = command.model; commands.push_back(command);
        }
    CameraData camera;
    camera.view = glm::lookAtRH(glm::vec3(0,3,10),glm::vec3(0,1,-2),glm::vec3(0,1,0));
    camera.projection = glm::perspectiveRH_ZO(glm::radians(65.f),1.f,.1f,100.f);
    BasicLighting light; light.shadowsEnabled = false;
    for (bool gpu : {false,true})
    {
        // Keep unrelated effects/pipeline compilation outside this skinning
        // comparison. Both modes use the same lit geometry shaders and draws.
        ShaderArtifactLibrary library;
        BasicRendererShaderPackage shaders;
        shaders.vertex = library.Load("BasicLit","vertex");
        shaders.instancedVertex = library.Load("BasicLitInstanced","vertex");
        shaders.fragment = library.Load("BasicLit","fragment");
        shaders.shadowVertex = library.Load("DirectionalShadow","vertex");
        shaders.shadowInstancedVertex = library.Load("DirectionalShadowInstanced","vertex");
        shaders.shadowFragment = library.Load("DirectionalShadow","fragment");
        shaders.displayOutput = {library.Load("DisplayOutput","vertex"),library.Load("DisplayOutput","fragment")};
        if (gpu) shaders.skinning = library.Load("GpuSkinning","compute");
        RhiSceneRenderer renderer;
        progress << (gpu ? "GPU" : "CPU") << " initializing renderer" << std::endl;
        renderer.SetSubmissionLabel(gpu ? "GPU skinning benchmark" : "CPU skinning benchmark");
        if (!renderer.Initialize(device,shaders)) throw std::runtime_error("Renderer initialization failed");
        progress << "Renderer ready" << std::endl;
        scene::AnimationComponent animation; animation.SetClipsFromImportedAnimations(clips); animation.Play("Rifle_Jog");
        double translation = 0, active = 0, skinGpu = 0;
        unsigned observations = 0; std::uint64_t previousObservation = 0;
        for (unsigned frame = 0; frame < 52; ++frame)
        {
            animation.Update(1.f/30);
            const auto &pose = animation.GetJointMatrices(mesh->GetSkeleton(),mesh->GetAnimationNodes());
            for (auto &palette : palettes)
            {
                palette = pose;
                // Benchmark changed-pose frames even if animation sampling
                // intentionally holds a baked pose for a rendering frame.
                palette[0][3].x += .0001f * frame;
            }
            if (!renderer.Render(256,256,camera,light,commands,commands)) throw std::runtime_error("Bot benchmark render failed");
            if (frame%10 == 0) progress << "Rendered frame " << frame << std::endl;
            const auto &timing = renderer.GetTimingStats();
            const auto rhi = device.GetTimingStats(gpu ? "GPU skinning benchmark" : "CPU skinning benchmark");
            if (frame == 0)
                std::cout << (gpu ? "GPU" : "CPU") << " initial: " << timing.skinningUpdateCount << " updates / "
                          << timing.gpuSkinningDispatches << " dispatches / " << timing.gpuSkinningPaletteBytes << " palette bytes\n";
            if (gpu && timing.gpuSkinningDispatches != 9) throw std::runtime_error("Expected nine GPU skinning dispatches");
            if (frame >= 12)
            {
                translation += timing.commandTranslationMs;
                active += timing.totalMs-rhi.frameFenceWaitMs;
                if (rhi.gpuObservationId != previousObservation)
                    for (const auto &scope : rhi.gpuScopes)
                        if (scope.name == "RHI GPU skinning") { skinGpu += scope.milliseconds; ++observations; }
                previousObservation = rhi.gpuObservationId;
            }
        }
        std::cout << (gpu ? "GPU" : "CPU") << " nine-bot renderer: translation " << translation/40
                  << " ms, active CPU " << active/40 << " ms";
        if (observations) std::cout << ", skinning GPU " << skinGpu/observations << " ms";
        std::cout << '\n';
        progress << (gpu ? "GPU" : "CPU") << " translation " << translation/40 << " ms; active CPU " << active/40
                 << " ms; GPU skinning " << (observations ? skinGpu/observations : 0) << " ms" << std::endl;
        // Complete measured geometry submissions before releasing fixtures.
        // These unmeasured empty frames recycle every in-flight slot.
        for (unsigned drain = 0; drain < 3; ++drain)
        {
            device.GetImmediateContext().BeginFrame();
            device.GetImmediateContext().Submit();
        }
        progress << "Geometry submissions drained" << std::endl;
    }
    progress << "Benchmark complete; releasing device/assets" << std::endl;
    return 0;
}
catch (const std::exception &error) { std::cerr << error.what() << '\n'; return 1; }
