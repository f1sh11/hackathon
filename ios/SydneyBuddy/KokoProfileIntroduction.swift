import SwiftUI

struct KokoProfileIntroduction:View {
    let begin:()->Void
    let skip:()->Void
    @State private var welcome=KoalaCue(event:.welcome)
    var body:some View {
        GeometryReader { geometry in
            let compact=geometry.size.height<700
            VStack(spacing:0) {
                Text("先认识一下彼此").font(.headline).padding(.top,12)
                KokoCurrentSpeech(text:"嗨，我是 Koko。咱们先用几个小选择认识一下，再开始这趟旅行。")
                    .padding(.horizontal,24).padding(.top,compact ? 10:28)
                Spacer(minLength:compact ? 6:20)
                KokoCompanionStage(cue:welcome,size:KokoCompanionSizing.characterSize(viewport:geometry.size),mood:.greeting)
                Spacer(minLength:compact ? 8:28)
                VStack(spacing:compact ? 7:12) {
                    Button(action:begin){Text("和 Koko 认识一下").font(.subheadline.weight(.semibold)).frame(maxWidth:.infinity,minHeight:compact ? 34:48)}
                        .buttonStyle(KokoOptionButtonStyle(prominent:true,compact:compact)).accessibilityIdentifier("koko-profile-start")
                    Button(action:skip){Text("先跳过，准备旅行").font(.subheadline.weight(.semibold)).frame(maxWidth:.infinity,minHeight:compact ? 34:48)}
                        .buttonStyle(KokoOptionButtonStyle(compact:compact)).accessibilityIdentifier("koko-profile-skip")
                    Text("不知道的先留白；长期偏好由你确认后再保存。已有记忆和旅行都会保留。")
                        .font(.caption).foregroundStyle(Palette.muted).multilineTextAlignment(.center).padding(.top,4)
                }.padding(.horizontal,24).padding(.bottom,20)
            }.frame(maxWidth:.infinity,maxHeight:.infinity)
        }.background(Palette.cream).foregroundStyle(Palette.ink).accessibilityIdentifier("koko-profile-introduction")
    }
}

struct KokoTripCompletion {
    var trip:Trip
    var cue=KoalaCue(event:.tripSaved)
}
enum KokoCompletionDestination {case viewTrip,adjustTrip,myTrips}

struct KokoTripCompletionView:View {
    let completion:KokoTripCompletion
    let choose:(KokoCompletionDestination)->Void
    var body:some View {
        GeometryReader { geometry in
            let compact=geometry.size.height<700
            VStack(spacing:0) {
                Text("行程已保存").font(.headline).padding(.top,12)
                KokoCurrentSpeech(text:"咱们一起定下来的行程，已经保存好了。想先看看，还是再调整一下？")
                    .padding(.horizontal,24).padding(.top,compact ? 10:28)
                Spacer(minLength:compact ? 6:20)
                KokoCompanionStage(cue:completion.cue,size:KokoCompanionSizing.characterSize(viewport:geometry.size),mood:.excited)
                Spacer(minLength:compact ? 6:20)
                Text(completion.trip.title).font(.subheadline).foregroundStyle(Palette.muted).multilineTextAlignment(.center).padding(.horizontal,24).padding(.bottom,20)
                VStack(spacing:compact ? 7:12) {
                    Button{choose(.viewTrip)}label:{Text("看看这趟行程").font(.subheadline.weight(.semibold)).frame(maxWidth:.infinity,minHeight:compact ? 34:48)}
                        .buttonStyle(KokoOptionButtonStyle(prominent:true,compact:compact)).accessibilityIdentifier("koko-completion-view-trip")
                    Button{choose(.adjustTrip)}label:{Text("再一起调整一下").font(.subheadline.weight(.semibold)).frame(maxWidth:.infinity,minHeight:compact ? 34:48)}
                        .buttonStyle(KokoOptionButtonStyle(compact:compact)).accessibilityIdentifier("koko-completion-adjust")
                    Button{choose(.myTrips)}label:{Text("回到我的旅程").font(.subheadline.weight(.semibold)).frame(maxWidth:.infinity,minHeight:compact ? 34:48)}
                        .buttonStyle(KokoOptionButtonStyle(compact:compact)).accessibilityIdentifier("koko-completion-my-trips")
                }.padding(.horizontal,24).padding(.bottom,20)
            }.frame(maxWidth:.infinity,maxHeight:.infinity)
        }.background(Palette.cream).foregroundStyle(Palette.ink).accessibilityIdentifier("koko-trip-completion")
    }
}
