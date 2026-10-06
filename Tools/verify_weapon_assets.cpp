#include "PlutoGE/assets/AssetManager.h"
#include "PlutoGE/render/Mesh.h"
#include "PlutoGE/scene/components/AnimationComponent.h"
#include <cmath>
#include <iostream>
#include <stdexcept>

// Uses the engine's actual binary readers and pose evaluator, without a GPU/window.
int main(int argc, char **argv) try
{
    if (argc != 2) return 2;
    PlutoGE::assets::AssetManager assets;
    assets.SetProjectContext(argv[1]);
    for (const std::string id : {"ar", "pistol", "lmg"})
    {
        const auto base = "project://Weapons/" + id + "/";
        auto *mesh = assets.LoadMeshAsset(base + id + ".plutomesh");
        if (!mesh || mesh->GetSkeleton().joints.size() != 6 || mesh->GetVertexCount() < 100)
            throw std::runtime_error("Invalid mesh/skeleton: " + id);
        std::vector<PlutoGE::render::AnimationClip> clips;
        if (!assets.LoadAnimationAsset(base + id + ".plutoanim", clips) || clips.size() != 5)
            throw std::runtime_error("Invalid animation set: " + id);
        for (const auto &clip : clips)
        {
            if (clip.channels.size() != 18 || clip.duration <= 0)
                throw std::runtime_error("Missing animation channels");
            for (const auto &channel : clip.channels)
                if (channel.times.empty() || channel.times.size() != channel.values.size() ||
                    channel.nodeIndex < 0 || channel.nodeIndex >= 6 || channel.targetName.empty())
                    throw std::runtime_error("Unbound animation channel");
        }
        if (clips[3].events.size() != 4 || clips[3].events[0].name != "ReloadMagOut" ||
            clips[3].events[1].name != "ReloadMagIn" || clips[3].events[2].name != "ReloadRack" ||
            clips[3].events[3].name != "ReloadFinish")
            throw std::runtime_error("Missing reload commit event");
        bool graphLoaded = false;
        const auto graph = assets.LoadAnimationGraphAsset(base + id + ".plutoanimgraph", &graphLoaded);
        if (!graphLoaded || graph.layers.size() != 4 || graph.parameters.size() != 5)
            throw std::runtime_error("Animation graph lost draw/fire/reload/sprint layers");
        PlutoGE::scene::AnimationComponent animation;
        animation.SetClipsFromImportedAnimations(clips);
        for (const auto &clip : clips)
        {
            if (!animation.Play(clip.name)) throw std::runtime_error("Clip playback failed");
            // Native graph sampling advances its state clock in Update; SetTime
            // alone only updates the direct clip clock and leaves graph time at zero.
            animation.Update(clip.duration * .2f);
            const auto &matrices = animation.GetJointMatrices(mesh->GetSkeleton(), mesh->GetAnimationNodes());
            if (matrices.size() != 6) throw std::runtime_error("Pose evaluation lost a joint");
            float deviation = 0;
            for (const auto &matrix : matrices)
                for (int column = 0; column < 4; ++column)
                    for (int row = 0; row < 4; ++row)
                    {
                        if (!std::isfinite(matrix[column][row])) throw std::runtime_error("Invalid skinning pose");
                        deviation += std::abs(matrix[column][row] - (column == row ? 1.f : 0.f));
                    }
            if (clip.name != id + "_Idle" && deviation < .01f)
                throw std::runtime_error("Authored animation did not move the rig: " + clip.name);
        }
        std::cout << "PASS native asset readers + five evaluated skinning poses: " << id << '\n';
    }
    return 0;
}
catch (const std::exception &error) { std::cerr << error.what() << '\n'; return 1; }
