import SwiftUI

enum KokoCompanionSizing {
    static func characterSize(viewport:CGSize)->CGFloat {
        max(110,min(viewport.width*0.60,viewport.height*(viewport.height<700 ? 0.22:0.25)))
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
                PetAvatar(mood:mood,cue:cue,size:size).padding(.bottom,8)
            }.frame(width:space.size.width,height:space.size.height)
        }.frame(minHeight:size*1.10)
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
