// CPU preparation benchmark for the capture's nine bots and three camera views.
// Includes deformation, vertex staging copies and shadow bounds, not GPU time.
#include "PlutoGE/assets/AssetManager.h"
#include "PlutoGE/render/Mesh.h"
#include "PlutoGE/scene/components/AnimationComponent.h"
#include "RhiSkinning.h"
#include <chrono>
#include <iostream>
#include <stdexcept>

int main(int argc, char **argv) try
{
    using namespace PlutoGE;
    using namespace render;
    if (argc != 2) return 2;
    assets::AssetManager assets; assets.SetProjectContext(argv[1]);
    auto *full = assets.LoadMeshAsset("project://Bots/Soldier/Soldier.plutomesh");
    auto *game = assets.LoadMeshAsset("project://Bots/Soldier/Soldier_Game.plutomesh");
    std::vector<AnimationClip> clips;
    if (!full || !game || !assets.LoadAnimationAsset("project://Bots/Soldier/Soldier.plutoanim", clips))
        throw std::runtime_error("Missing bot benchmark assets");
    scene::AnimationComponent animation; animation.SetClipsFromImportedAnimations(clips);
    animation.Play("Rifle_Jog"); animation.Update(.2f);
    const auto palette = animation.GetJointMatrices(full->GetSkeleton(), full->GetAnimationNodes());
    RhiSkinningExecutor executor(4);
    double checksum = 0;
    const auto benchmark = [&](const Mesh &mesh, bool legacy)
    {
        std::array<std::vector<BasicVertex>, 9> vertices, staging;
        std::array<RhiSkinningJob, 9> jobs;
        std::array<std::vector<ShadowGeometryCluster>, 9> clusters;
        RhiSkinnedShadowBounds bounds;
        bounds.Build(mesh.GetMeshData().vertices, mesh.GetMeshData().indices, palette.size());
        for (std::size_t i = 0; i < jobs.size(); ++i)
            jobs[i] = {mesh.GetMeshData().vertices, palette, {}, &vertices[i]};
        double elapsed = 0;
        for (unsigned frame = 0; frame < 23; ++frame)
        {
            const auto start = std::chrono::steady_clock::now();
            for (unsigned view = 0; view < (legacy ? 3u : 1u); ++view)
            {
                for (std::size_t i = 0; i < jobs.size(); ++i) jobs[i].previous = vertices[i];
                executor.DeformBatch(jobs);
                for (std::size_t i = 0; i < jobs.size(); ++i)
                {
                    staging[i].assign(vertices[i].begin(), vertices[i].end());
                    if (legacy) clusters[i] = BuildShadowGeometryClusters<BasicVertex>(vertices[i], mesh.GetMeshData().indices);
                    else bounds.Refit(palette, clusters[i]);
                    checksum += staging[i][frame].position[0] + clusters[i][0].center.x;
                }
            }
            if (frame >= 3) elapsed += std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count();
        }
        return elapsed / 20;
    };
    const auto before = benchmark(*full, true);
    const auto rendererOnly = benchmark(*full, false);
    const auto after = benchmark(*game, false);
    std::cout << "9 bots / 3 views CPU preparation: " << before << " ms baseline -> "
              << rendererOnly << " ms renderer fix -> " << after << " ms with game mesh\n"
              << "Vertices per bot: " << full->GetVertexCount() << " -> " << game->GetVertexCount()
              << "; checksum=" << checksum << '\n';
    return 0;
}
catch (const std::exception &e) { std::cerr << e.what() << '\n'; return 1; }
