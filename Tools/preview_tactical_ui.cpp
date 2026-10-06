// Native RmlUi layout/interaction checks and a CPU triangle renderer for reproducible UI previews.
#include <RmlUi/Core.h>
#include <algorithm>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <map>
#include <set>
#include <stdexcept>
#define STB_IMAGE_IMPLEMENTATION
#include <stb_image.h>
#define STB_IMAGE_WRITE_IMPLEMENTATION
#include <stb_image_write.h>

struct Geometry { std::vector<Rml::Vertex> vertices; std::vector<int> indices; };
struct Texture { int width, height; std::vector<unsigned char> pixels; };
class Renderer final : public Rml::RenderInterface {
public:
    int width = 1280, height = 720;
    std::vector<unsigned char> pixels;
    bool clipping = false;
    Rml::Rectanglei clip;
    std::map<uintptr_t, Geometry> meshes;
    std::map<uintptr_t, Texture> textures;
    uintptr_t next = 1;
    void Clear(int w, int h) {
        width = w; height = h; pixels.resize(w*h*4);
        for (int i=0;i<w*h;i++) { pixels[i*4]=14; pixels[i*4+1]=18; pixels[i*4+2]=21; pixels[i*4+3]=255; }
    }
    Rml::CompiledGeometryHandle CompileGeometry(Rml::Span<const Rml::Vertex> v, Rml::Span<const int> i) override {
        auto id=next++; meshes[id]={{v.begin(),v.end()},{i.begin(),i.end()}}; return id;
    }
    void ReleaseGeometry(Rml::CompiledGeometryHandle id) override { meshes.erase(id); }
    Rml::TextureHandle GenerateTexture(Rml::Span<const Rml::byte> bytes, Rml::Vector2i size) override {
        auto id=next++; textures[id]={size.x,size.y,{bytes.begin(),bytes.end()}}; return id;
    }
    Rml::TextureHandle LoadTexture(Rml::Vector2i& size, const Rml::String& path) override {
        int channels; auto data=stbi_load(path.c_str(), &size.x,&size.y,&channels,4);
        if (!data) throw std::runtime_error("Missing texture: "+path);
        // RmlUi render interface consumes premultiplied textures.
        for (int i=0;i<size.x*size.y;i++) for(int c=0;c<3;c++) data[i*4+c]=data[i*4+c]*data[i*4+3]/255;
        auto id=GenerateTexture({data, size_t(size.x*size.y*4)},size); stbi_image_free(data); return id;
    }
    void ReleaseTexture(Rml::TextureHandle id) override { textures.erase(id); }
    void EnableScissorRegion(bool enabled) override { clipping=enabled; }
    void SetScissorRegion(Rml::Rectanglei rectangle) override { clip=rectangle; }
    static float Edge(Rml::Vector2f a,Rml::Vector2f b,Rml::Vector2f p) { return (p.x-a.x)*(b.y-a.y)-(p.y-a.y)*(b.x-a.x); }
    void RenderGeometry(Rml::CompiledGeometryHandle id,Rml::Vector2f translation,Rml::TextureHandle tex) override {
        const auto& mesh=meshes.at(id);
        const Texture* texture=tex ? &textures.at(tex) : nullptr;
        for(size_t n=0;n<mesh.indices.size();n+=3) {
            auto a=mesh.vertices[mesh.indices[n]], b=mesh.vertices[mesh.indices[n+1]], c=mesh.vertices[mesh.indices[n+2]];
            a.position+=translation;b.position+=translation;c.position+=translation;
            float area=Edge(a.position,b.position,c.position); if(std::abs(area)<.0001f) continue;
            int x0=std::max(0,int(std::floor(std::min({a.position.x,b.position.x,c.position.x}))));
            int x1=std::min(width,int(std::ceil(std::max({a.position.x,b.position.x,c.position.x}))));
            int y0=std::max(0,int(std::floor(std::min({a.position.y,b.position.y,c.position.y}))));
            int y1=std::min(height,int(std::ceil(std::max({a.position.y,b.position.y,c.position.y}))));
            if(clipping) { x0=std::max(x0,clip.Left());x1=std::min(x1,clip.Right());y0=std::max(y0,clip.Top());y1=std::min(y1,clip.Bottom()); }
            for(int y=y0;y<y1;y++) for(int x=x0;x<x1;x++) {
                Rml::Vector2f p(float(x)+.5f,float(y)+.5f);
                float u=Edge(b.position,c.position,p)/area,v=Edge(c.position,a.position,p)/area,w=1-u-v;
                if(u<0 || v<0 || w<0) continue;
                float color[4]={u*a.colour.red+v*b.colour.red+w*c.colour.red,u*a.colour.green+v*b.colour.green+w*c.colour.green,
                    u*a.colour.blue+v*b.colour.blue+w*c.colour.blue,u*a.colour.alpha+v*b.colour.alpha+w*c.colour.alpha};
                if(texture) {
                    auto uv=a.tex_coord*u+b.tex_coord*v+c.tex_coord*w;
                    int tx=std::clamp(int(uv.x*texture->width),0,texture->width-1),ty=std::clamp(int(uv.y*texture->height),0,texture->height-1);
                    for(int k=0;k<4;k++) color[k]*=texture->pixels[(ty*texture->width+tx)*4+k]/255.f;
                }
                auto offset=(y*width+x)*4;
                for(int k=0;k<3;k++) pixels[offset+k]=(unsigned char)std::clamp(color[k]+pixels[offset+k]*(1-color[3]/255.f),0.f,255.f);
            }
        }
    }
};
class System final : public Rml::SystemInterface {
public:
    bool invalid=false;
    bool LogMessage(Rml::Log::Type type,const Rml::String& message) override {
        std::cerr<<message<<'\n';
        if(type==Rml::Log::LT_ERROR || type==Rml::Log::LT_ASSERT || message.find("Syntax error")!=std::string::npos || message.find("Invalid at-rule")!=std::string::npos) invalid=true;
        return true;
    }
};
class Click final : public Rml::EventListener {
public: int count=0; void ProcessEvent(Rml::Event&) override { count++; }
};
int main(int argc,char** argv) try {
    if(argc!=2) throw std::runtime_error("Usage: preview_tactical_ui <project-root>");
    std::filesystem::path root=argv[1], out=root/"Tools/Native/UI";
    std::filesystem::create_directories(out);
    System system;Renderer renderer;Rml::SetSystemInterface(&system);Rml::SetRenderInterface(&renderer);
    if(!Rml::Initialise()) throw std::runtime_error("RmlUi init failed");
    std::vector<std::vector<Rml::byte>> fontData;
    for(auto [file,family]:std::vector<std::pair<std::string,std::string>>{{"Inter.ttf","TacticalBody"},{"Rajdhani-Regular.ttf","TacticalDisplay"},{"Rajdhani-Bold.ttf","TacticalHeading"}}) {
        std::ifstream stream(root/"Assets/Fonts"/file,std::ios::binary);
        fontData.emplace_back(std::istreambuf_iterator<char>(stream),std::istreambuf_iterator<char>());
        if(!Rml::LoadFontFace(fontData.back(),family,Rml::Style::FontStyle::Normal,Rml::Style::FontWeight::Auto)) throw std::runtime_error("Font load failed: "+file);
    }
    auto context=Rml::CreateContext("Tactical UI",{1280,720});
    for(std::string screen:{"title","pause-menu","inventory","hud","hardpoint-marker","defusal-marker","friendly-nameplate"}) {
        auto document=context->LoadDocument((root/"Assets/UI"/(screen+".rml")).string());
        if(!document) throw std::runtime_error("Document load failed");
        document->Show();
        auto set=[&](const char* id,const char* markup) { auto e=document->GetElementById(id);if(e) e->SetInnerRML(markup); };
        if(auto marker=document->GetElementById("marker")) {
            marker->SetClass("hidden",false);
            marker->SetInnerRML("<img class=\"marker-icon\" src=\"UI/Icons/objective.tga\"/>A / COURTYARD / 42m");
        }
        if(screen=="inventory") {
            const char* stats[]={"28 DMG / 180m / 660 RPM / 30 RND","34 DMG / 90m / 360 RPM / 12 RND","23 DMG / 200m / 780 RPM / 60 RND"};
            const char* names[]={"AMMUNITION","MED KIT","ARMOUR PLATE"};
            const char* icons[]={"ammo","health","armour"};
            for(int i=0;i<3;i++) {
                set(("weapon-stats-"+std::to_string(i)).c_str(),stats[i]);
                auto slot=document->GetElementById("slot-"+std::to_string(i));slot->SetClass("occupied",true);
                slot->SetInnerRML(std::string("<img class=\"item-icon\" src=\"UI/Icons/")+icons[i]+".tga\"/><div class=\"item-type\">FIELD SUPPLY</div><div class=\"item-name\">"+names[i]+"</div><div class=\"item-count\">x"+(i==0 ? "150" : "1")+"</div>");
            }
        }
        if(screen=="hud") {
            set("objective-status","CAPTURE / COURTYARD");set("objective-detail","NEUTRAL / 42m / NEXT ROTATION 00:35");set("objective-instruction","Hold the zone to score. Both teams inside = contested.");
            set("objective-rotation","CAPTURING / 62%");set("alpha-score","4");set("bravo-score","2");set("health-value","86");
            document->GetElementById("health")->SetAttribute("value",86);
            document->GetElementById("armour-0")->SetClass("filled",true);
            document->GetElementById("armour-1")->SetClass("filled",true);
            set("kill-feed","<div class=\"feed-entry\"><span class=\"alpha\">NOMAD</span> &#160; AR-24 &#160; <span class=\"bravo\">VIPER</span></div>");
            set("supply-counts","H MED KITS 1 / G PLATES 2 / CAPACITY 4/8");set("supply-use","E COLLECT SUPPLIES / I INVENTORY");
            set("interaction","<span class=\"ui-key\">E</span> COLLECT AMMUNITION");
            set("compass-ticks","<span class=\"compass-tick\">285</span><span class=\"compass-tick\">300</span><span class=\"compass-tick\">NW</span><span class=\"compass-tick\">330</span><span class=\"compass-tick\">345</span>");set("compass-heading","318");
        }
        for(auto size:{Rml::Vector2i(960,540),Rml::Vector2i(1280,720),Rml::Vector2i(1920,1080)}) {
            context->SetDimensions(size);context->Update();context->Update();
            Rml::ElementList controls;document->GetElementsByTagName(controls,"button");
            for(auto button:controls) {
                auto p=button->GetAbsoluteOffset(Rml::BoxArea::Border),s=button->GetBox().GetSize(Rml::BoxArea::Border);
                if(p.x<0 || p.y<0 || p.x+s.x>size.x+1 || p.y+s.y>size.y+1 || s.x<40 || s.y<30) throw std::runtime_error("Clipped/collapsed control: "+screen+"/"+button->GetId()+" at "+std::to_string(size.x));
                Click probe;button->AddEventListener(Rml::EventId::Click,&probe);
                context->ProcessMouseMove(int(p.x+s.x*.5f),int(p.y+s.y*.5f),0);context->ProcessMouseButtonDown(0,0);context->ProcessMouseButtonUp(0,0);
                button->RemoveEventListener(Rml::EventId::Click,&probe);
                if(probe.count!=1) throw std::runtime_error("Native hit test failed: "+button->GetId());
            }
            document->Focus();
            context->ProcessMouseMove(0,0,0);context->Update();renderer.Clear(size.x,size.y);context->Render();
            auto file=out/(screen+"-"+std::to_string(size.x)+".png");
            stbi_write_png(file.string().c_str(),size.x,size.y,4,renderer.pixels.data(),size.x*4);
        }
        document->Close();context->Update();
        std::cout<<"PASS: "<<screen<<" native layout, controls, 540p/720p/1080p renders\n";
    }
    Rml::Shutdown();
    return system.invalid ? 1 : 0;
} catch(const std::exception& e) { std::cerr<<e.what()<<'\n'; return 1; }
