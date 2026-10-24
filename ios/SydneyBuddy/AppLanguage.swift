import Foundation

enum AppLanguage:String,CaseIterable,Identifiable {
    case traditional="zh-Hant",simplified="zh-Hans",english="en"
    var id:String{rawValue}
    var label:String{switch self{case .traditional:return "繁體中文";case .simplified:return "简体中文";case .english:return "English"}}
    func text(_ key:String,_ arguments:String...)->String {
        let bundle=Bundle.main.path(forResource:rawValue,ofType:"lproj").flatMap{Bundle(path:$0)} ?? .main
        var result=bundle.localizedString(forKey:key,value:key,table:nil)
        for (i,value) in arguments.enumerated(){result=result.replacingOccurrences(of:"{\(i)}",with:value)}
        return result
    }
}
