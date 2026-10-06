#include "PlutoGE/assets/AssetManager.h"
#include "PlutoGE/core/Engine.h"
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
    if (!loaded || graph.layers.size()!=5 || graph.states.size()!=4 || graph.parameters.size()!=6) throw std::runtime_error("Invalid rifle graph");
    auto &engineAssets=PlutoGE::core::Engine::GetInstance().GetAssetManager();
    engineAssets.SetProjectContext(argv[1]);
    PlutoGE::scene::AnimationComponent layered;
    layered.SetClipsFromImportedAnimations(clips);
    if (!layered.SetAnimationGraphAssetReference(base+"Soldier.plutoanimgraph"))
        throw std::runtime_error("Graph binding failed");
    if(layered.GetClipCount()<12) throw std::runtime_error("UAL clips were not loaded into graph");
    for(const auto &state:layered.GetGraphStates())
        if(state.clipIndex<0 || state.clipIndex>=layered.GetClipCount() ||
           layered.GetClips()[state.clipIndex].name.rfind("Rifle_",0)==0)
            throw std::runtime_error("UAL state fell back to generated rifle locomotion");
    for (float speed : {0.f, 1.f, 4.f, 7.f, 0.f})
    {
        layered.SetFloat("MovementSpeed",speed);
        std::vector<glm::mat4> previous;
        float legMotion=0;
        for (int frame=0;frame<90;++frame)
        {
            if(frame==20) layered.SetBool("HasTarget",true);
            if(frame==30) layered.SetTrigger("Shoot");
            if(frame==40) layered.SetTrigger("Hit");
            if(frame==50) { layered.SetFloat("SupportHandIK",0); layered.SetTrigger("Reload"); }
            if(frame==80) { layered.SetFloat("SupportHandIK",1); layered.SetBool("HasTarget",false); }
            layered.Update(1.f/30.f);
            const auto &pose=layered.GetJointMatrices(mesh->GetSkeleton(),mesh->GetAnimationNodes());
            if(frame>=60 && !previous.empty())
                for(const auto &mapping:mesh->GetSkeleton().humanoidBoneMappings)
                    if(mapping.bone==PlutoGE::render::HumanoidBone::LeftUpperLeg || mapping.bone==PlutoGE::render::HumanoidBone::RightUpperLeg)
                        for(int c=0;c<4;++c) for(int r=0;r<4;++r)
                            legMotion+=std::abs(pose[mapping.targetJointIndex][c][r]-previous[mapping.targetJointIndex][c][r]);
            previous=pose;
            if(pose.size()!=63) throw std::runtime_error("Invalid UAL layered pose");
            for(const auto &matrix:pose) for(int c=0;c<4;++c) for(int r=0;r<4;++r)
                if(!std::isfinite(matrix[c][r])) throw std::runtime_error("Nonfinite UAL layered pose");
        }
        if(speed>.05f && legMotion<.01f) throw std::runtime_error("UAL locomotion has no leg animation");
        const int expected=speed<.05f?0:speed<2.4f?1:speed<5.8f?2:3;
        if(layered.GetCurrentStateIndex()!=expected) throw std::runtime_error("UAL locomotion transition failed");
        std::cout<<"PASS UAL locomotion, masked weapon layers: "<<speed<<" m/s\n";
    }
    return 0;
}
catch (const std::exception &e) { std::cerr<<e.what()<<'\n'; return 1; }
