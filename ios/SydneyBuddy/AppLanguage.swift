import Foundation

enum AppLanguage:String,CaseIterable,Identifiable {
    case traditional="zh-Hant",simplified="zh-Hans",english="en"
    var id:String{rawValue}
    var label:String{switch self{case .traditional:return "繁體中文";case .simplified:return "简体中文";case .english:return "English"}}
    static var current:AppLanguage { AppLanguage(rawValue:UserDefaults.standard.string(forKey:"appLanguage") ?? "") ?? .simplified }
    private var bundle:Bundle { Self.bundles[self] ?? .main }
    private static let bundles:[AppLanguage:Bundle] = Dictionary(uniqueKeysWithValues:allCases.compactMap { language in
        Bundle.main.path(forResource:language.rawValue,ofType:"lproj").flatMap { Bundle(path:$0) }.map { (language,$0) }
    })
    private static let rendered:NSCache<NSString,NSString> = {
        let cache=NSCache<NSString,NSString>();cache.countLimit=1200;return cache
    }()
    func display(_ value:String)->String {
        guard !value.isEmpty else{return value}
        let key=(rawValue+"\u{0}"+value) as NSString
        if let cached=Self.rendered.object(forKey:key){return cached as String}
        let result=render(value);Self.rendered.setObject(result as NSString,forKey:key);return result
    }
    private func render(_ value:String)->String {
        let translated=bundle.localizedString(forKey:value,value:value,table:nil)
        if translated != value { return translated }
        // Summary parts are localized individually; field values stay unchanged in storage.
        if self == .english {
            for prefix in ["收到啦～","可选补充：","我记得你：","本次兴趣：","仍未知："] where value.hasPrefix(prefix) && value.count>prefix.count {
                let localized=bundle.localizedString(forKey:prefix,value:prefix,table:nil)
                if localized != prefix {return localized+display(String(value.dropFirst(prefix.count)))}
            }

        }
        // Dynamic summaries and server-supplied canned questions use the same catalog templates.
        if value.unicodeScalars.contains(where:{(0x3400...0x9FFF).contains($0.value)}) {
            for entry in Self.templates[self] ?? [] {
                let range=NSRange(value.startIndex...,in:value)
                guard let match=entry.pattern.firstMatch(in:value,range:range) else { continue }
                var arguments:[Int:String]=[:]
                for (offset,index) in entry.indices.enumerated() {
                    guard let capture=Range(match.range(at:offset+1),in:value) else {continue}
                    let argument=String(value[capture]);arguments[index]=argument == value ? argument:display(argument)
                }
                return format(entry.translation,arguments:arguments)
            }
        }
        if self == .english {
            // Backend-owned fixed paragraphs are composed before delivery. Translate complete
            // known sentences, while leaving unknown content and user-authored messages intact.
            for fragment in Self.sentences where value.count>fragment.source.count && value.hasPrefix(fragment.source) {
                let tail=display(String(value.dropFirst(fragment.source.count)))
                let gap=fragment.translation.last.map{".?!:;~".contains($0)} == true && tail.first?.isLetter == true ? " ":""
                return fragment.translation+gap+tail
            }
        }
        if self == .english {
            for separator in [" · ","、","\n"] where value.contains(separator) {
                return value.components(separatedBy:separator).map{display($0)}.joined(separator:separator == "、" ? ", ":separator)
            }
            if value.hasPrefix("· "){return "· "+display(String(value.dropFirst(2)))}
            if let colon=value.firstIndex(of:"："),value.distance(from:value.startIndex,to:colon)<20 {
                let label=String(value[..<colon]);let localized=bundle.localizedString(forKey:label,value:label,table:nil)
                if localized != label {return localized+": "+display(String(value[value.index(after:colon)...]))}
            }
        }
        switch self {
        case .simplified:return value.applyingTransform(StringTransform("Traditional-Simplified"),reverse:false) ?? value
        case .traditional:return value.applyingTransform(StringTransform("Simplified-Traditional"),reverse:false) ?? value
        case .english:return value // Unknown content and proper names are preserved, never guessed.
        }
    }
    private static let sentences:[(source:String,translation:String)] = {
        guard let path=bundles[.english]?.path(forResource:"Localizable",ofType:"strings"),let data=try? Data(contentsOf:URL(fileURLWithPath:path)),let entries=(try? PropertyListSerialization.propertyList(from:data,format:nil)) as? [String:String] else{return []}
        return entries.filter { key,value in
            key.count>=4 && !key.contains("{") && key.unicodeScalars.contains(where:{(0x3400...0x9FFF).contains($0.value)}) && key.last.map{"。？！～：".contains($0)} == true
        }.map{(source:$0.key,translation:$0.value)}.sorted{$0.source.count>$1.source.count}
    }()
    private struct Template {
        let pattern:NSRegularExpression
        let indices:[Int]
        let translation:String
    }
    private static let templates:[AppLanguage:[Template]] = Dictionary(uniqueKeysWithValues:allCases.map { language in
        let path=bundles[language]?.path(forResource:"Localizable",ofType:"strings")
        let entries=path.flatMap { try? Data(contentsOf:URL(fileURLWithPath:$0)) }.flatMap {
            (try? PropertyListSerialization.propertyList(from:$0,format:nil)) as? [String:String]
        } ?? [:]
        let token=try! NSRegularExpression(pattern:#"\{(\d+)\}"#)
        let values=entries.sorted { $0.key.count > $1.key.count }.compactMap { key,translation -> Template? in
            let matches=token.matches(in:key,range:NSRange(key.startIndex...,in:key))
            guard !matches.isEmpty else {return nil}
            var pattern="\\A";var cursor=key.startIndex;var indices:[Int]=[]
            for match in matches {
                guard let range=Range(match.range,in:key),let number=Range(match.range(at:1),in:key),let index=Int(key[number]) else {return nil}
                pattern+=NSRegularExpression.escapedPattern(for:String(key[cursor..<range.lowerBound]))+"(.+?)"
                cursor=range.upperBound;indices.append(index)
            }
            pattern+=NSRegularExpression.escapedPattern(for:String(key[cursor...]))+"\\z"
            guard let expression=try? NSRegularExpression(pattern:pattern,options:[.dotMatchesLineSeparators]) else{return nil}
            return Template(pattern:expression,indices:indices,translation:translation)
        }
        return (language,values)
    })
    private static let placeholder=try! NSRegularExpression(pattern:#"\{(\d+)\}"#)
    private static func interpolate(_ template:String,arguments:[Int:String])->String {
        var output=template
        for match in placeholder.matches(in:template,range:NSRange(template.startIndex...,in:template)).reversed() {
            guard let number=Range(match.range(at:1),in:template),let index=Int(template[number]),let value=arguments[index],let range=Range(match.range,in:output) else {continue}
            output.replaceSubrange(range,with:value)
        }
        return output
    }
    private func format(_ template:String,arguments:[Int:String])->String {
        var result=Self.interpolate(template,arguments:arguments)
        if self == .english {
            for (plural,singular) in [("days","day"),("minutes","minute"),("hours","hour"),("stops","stop"),("people","person")] {
                result=result.replacingOccurrences(of:"(?<![0-9.])1 "+plural+"\\b",with:"1 "+singular,options:.regularExpression)
            }
        }
        return result
    }
    func text(_ key:String,_ arguments:String...)->String {
        format(display(key),arguments:Dictionary(uniqueKeysWithValues:arguments.enumerated().map{($0.offset,$0.element)}))
    }
}

/// Presentation-only localization. Never use this to build API fields or stored preference values.
func uiText(_ value:String)->String { AppLanguage.current.display(value) }
