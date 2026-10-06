#include "PlutoGE/assets/AssetManager.h"
#include "PlutoGE/render/Mesh.h"
#include "PlutoGE/scene/components/AnimationComponent.h"
#include <cmath>
#include <iostream>
#include <stdexcept>
int main(int argc, char **argv) try
{
    if (argc!=2) return 2;
    PlutoGE::assets::AssetManager assets; assets.SetProjectContext(argv[1]);
    const std::string base="project://Bots/Soldier/";
    auto *original=assets.LoadMeshAsset(base+"Soldier.plutomesh");
    auto *mesh=assets.LoadMeshAsset(base+"Soldier_Game.plutomesh");
    if (!original || !mesh || mesh->GetVertexCount() >= original->GetVertexCount() * .8)
        throw std::runtime_error("Game soldier did not reduce vertex work by at least 20 percent");
    auto *gun=assets.LoadMeshAsset(base+"M16_HandSpace.plutomesh");
    if (!mesh || mesh->GetSkeleton().joints.size()!=63 || mesh->GetSkeleton().humanoidBoneMappings.size()!=22 || mesh->GetSubmeshCount()!=46 || !gun || !gun->GetSkeleton().joints.empty())
        throw std::runtime_error("Invalid soldier skin, mappings or attachment mesh");
    std::vector<PlutoGE::render::AnimationClip> clips;
    if (!assets.LoadAnimationAsset(base+"Soldier.plutoanim",clips) || clips.size()!=8) throw std::runtime_error("Missing clips");
    PlutoGE::scene::AnimationComponent animation; animation.SetClipsFromImportedAnimations(clips);
    for (const auto &clip:clips)
    {
        if (clip.channels.size()!=189 || !animation.Play(clip.name)) throw std::runtime_error("Invalid clip channels");
        for (int sample=0;sample<5;++sample)
        {
            animation.Update(clip.duration*.2f);
            const auto &pose=animation.GetJointMatrices(mesh->GetSkeleton(),mesh->GetAnimationNodes());
            if (pose.size()!=63) throw std::runtime_error("Missing joint palette");
            for (const auto &matrix:pose) for (int c=0;c<4;++c) for (int r=0;r<4;++r)
                if (!std::isfinite(matrix[c][r])) throw std::runtime_error("Nonfinite pose");
        }
        std::cout<<"PASS native skinning: "<<clip.name<<'\n';
    }
    bool loaded=false; const auto graph=assets.LoadAnimationGraphAsset(base+"Soldier.plutoanimgraph",&loaded);
    if (!loaded || graph.layers.size()!=4 || graph.states.size()!=4 || graph.parameters.size()!=6) throw std::runtime_error("Invalid rifle graph");
    return 0;
}
catch (const std::exception &e) { std::cerr<<e.what()<<'\n'; return 1; }
