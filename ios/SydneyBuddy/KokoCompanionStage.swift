import SwiftUI

enum KokoCompanionSizing {
    // The approved Idle body spans y=50...233 inside its transparent 256px canvas.
    static let visibleHeightRatio:CGFloat = 183.0/256.0
    static func characterSize(viewport:CGSize)->CGFloat {
        let bodyHeight=viewport.height*(viewport.height<700 ? 0.22:0.28)
        return max(110,min(viewport.width*0.80,bodyHeight/visibleHeightRatio))
    }
}

struct KokoCompanionStage:View {
    let cue:KoalaCue?
    let size:CGFloat
    var mood:PetMood = .listening
    var body:some View {
        GeometryReader { space in
            ZStack(alignment:.bottom){
                Image("KokoCoastalBackground").resizable().scaledToFill()
                    .frame(width:space.size.width,height:space.size.height).clipped()
                    .mask(LinearGradient(stops:[.init(color:.clear,location:0),.init(color:.white,location:0.16),.init(color:.white,location:0.86),.init(color:.clear,location:1)],startPoint:.top,endPoint:.bottom))
                    .accessibilityHidden(true)
                // Anchor the character's feet, rather than the transparent canvas edge.
                PetAvatar(mood:mood,cue:cue,size:size)
                    .offset(y:size*(23.0/256.0+4.0/108.0)).padding(.bottom,8)
            }.frame(width:space.size.width,height:space.size.height)
        }.frame(minHeight:size*233.0/256.0+16)
    }
}

struct KokoOptionButtonStyle:ButtonStyle {
    var prominent=false
    var compact=false
    func makeBody(configuration:Configuration)->some View {
        configuration.label
            .foregroundStyle(prominent ? .white:Palette.ink)
            .frame(maxWidth:.infinity,minHeight:compact ? 34:44)
            .padding(.horizontal,16).padding(.vertical,compact ? 5:7)
            .background(RoundedRectangle(cornerRadius:18).fill(prominent ? Palette.coral:Palette.cream))
            .overlay(RoundedRectangle(cornerRadius:18).stroke(Palette.coral.opacity(0.8),lineWidth:1.5))
            .background(RoundedRectangle(cornerRadius:18).fill(Palette.coral).offset(y:configuration.isPressed ? 0:5))
            .offset(y:configuration.isPressed ? 4:0).padding(.bottom,5)
            .animation(.easeOut(duration:0.10),value:configuration.isPressed)
    }
}
