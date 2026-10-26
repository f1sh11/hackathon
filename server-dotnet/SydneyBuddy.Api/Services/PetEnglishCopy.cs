using System.Text.RegularExpressions;
using System.Text.Json;

namespace SydneyBuddy.Api;

/// Updates recognised application copy in old drafts without translating user messages or evidence.
/// Historical free-form model prose has no reliable translation and is deliberately left intact.
internal static class PetEnglishCopy
{
    private static readonly Dictionary<string,string> Copy=new()
    {
        ["这次按每人每天 {amount} AUD 活动预算来安排，不含住宿、交通和全部餐饮。"]="We’ll use an activity budget of A${amount} per person per day. Accommodation, transport and meals are separate.",
        ["收到啦～{question}"]="Noted. {question}",
        ["目的地待定"]="Destination to decide",
        ["{name}，咱们先找一点这趟旅行的灵感。先挑一种玩法：绿意慢走，还是城市故事？也可以换一组。"]="{name}, let's find a little inspiration for this trip. Would you prefer a nature stroll or the city's stories? We can try another idea too.",
        ["{labels}收好啦。"]="{labels} updated.",
        ["先给你一版「{direction}」方向，具体资料可以接着补。"]="Here's a first {direction} idea. We can add the trip details next.",
        ["那这次就按「{direction}」来准备。这个玩法只用于这次旅行。"]="Let's plan around {direction} for this trip. This choice applies to this trip only.",
        ["换个具体方向：「{direction}」。{idea}这个玩法怎么样？"]="Here's another direction: {direction}. {idea} What do you think?",
        ["我记得你确认过「{habit}」。这次也参考它吗？想换换也可以。"]="You previously confirmed {habit}. Shall we use that for this trip too? It's fine to change it.",
        ["每天约 {steps} 步"]="about {steps} steps a day",
        ["这一天按 {time} 前结束来试算。先看建议，确认后才保存；其他日期和长期习惯保留。"]="I'll work towards a {time} finish for this day. You'll see suggestions before confirming any saved changes; other days and long-term habits stay as they are.",
        ["收到！预算 {amount} {currency}（{basis}），先收进这次的小背包。换算为每人每日活动预算约 {daily} AUD。"]="Noted: {amount} {currency}, {basis}, for this trip. That’s about {daily} AUD per person per day for activities.",
        ["\n我记得你确认过「"]="\nYou previously confirmed ",
        [" AUD 活动预算来安排，不含住宿、交通和全部餐饮。"]=" AUD per person per day for activities. Accommodation, transport and the full meal budget are separate.",
        [" 前结束来试算。先看建议，确认后才保存；其他日期和长期习惯保留。"]=" finish for this day. You'll see suggestions before confirming any saved changes; other days and long-term habits stay as they are.",
        [" 步"]=" steps a day",
        ["1 人"]="1 person",
        ["120 AUD / 人 / 天"]="120 AUD / person / day",
        ["14:00 结束"]="Finish at 14:00",
        ["16:00 结束"]="Finish at 16:00",
        ["18:00 结束"]="Finish at 18:00",
        ["2 人"]="2 people",
        ["60 AUD / 人 / 天"]="60 AUD / person / day",
        ["{d.Nickname}，咱们先找一点这趟旅行的灵感。"]="{d.Nickname}, let's find a little inspiration for this trip. ",
        ["{name}，咱们先挑一种玩法：绿意慢走，还是城市故事？你也可以自己说。"]="{name}, shall we start with a nature stroll or the city's stories? You can suggest something else too.",
        ["。随时可以在记忆页修改或撤回。"]=". You can edit or remove this on the Memories page anytime.",
        ["」。"]=". ",
        ["」。这次也参考它吗？想换换也可以。"]=". Shall we use that for this trip too? It's fine to change it.",
        ["」方向，具体资料可以接着补。"]=" idea. We can add the trip details next.",
        ["」来准备。这个玩法只用于这次旅行。"]=" for this trip. This choice applies to this trip only.",
        ["一个人"]="Solo",
        ["不用努力打卡"]="no need to tick everything off",
        ["两个人"]="Two people",
        ["中央车站"]="Central Station",
        ["人数"]="Party size",
        ["今天"]="Today",
        ["保存或连接失败，请重新加载对话确认进度后重试。"]="Saving or connecting failed. Reload the conversation to check your progress, then retry.",
        ["保留草稿"]="Keep draft",
        ["修改城市"]="Change city",
        ["修改旅行资料"]="Edit trip details",
        ["候选偏好已保留在本次对话，H-MEM 暂未同步；确认保存时会重试。"]="Your preference candidates are kept in this conversation. H-MEM hasn’t synced yet; we’ll retry when you confirm saving.",
        ["先不改时间"]="Keep the original time",
        ["先把已有想法整理成灵感方向，细节可以之后一起补。"]="Let's turn your ideas into a direction for the trip. We can add the details together later.",
        ["先把这趟的方向摆出来了。下面是可以一起调整的灵感，具体场所、时间和费用等资料齐了再核实。"]="Here's a starting direction we can shape together. Once the trip details are ready, we'll check specific places, times and costs.",
        ["先挑一种玩法：绿意慢走，还是城市故事？也可以换一组。"]="Would you prefer a nature stroll or the city's stories? We can try another idea too.",
        ["先用已有的线索构想一版，未知的资料可以之后一起补。"]="Let's sketch a draft from what we know. We can fill in the missing details together later.",
        ["先看已有的草案"]="Show the draft so far",
        ["先确认至少一项旅行偏好，再完成 Travel DNA。"]="Confirm at least one travel preference before completing your Travel DNA.",
        ["先给你一版「"]="Here's a first ",
        ["先聊旅行"]="Let's talk about the trip",
        ["先说想去哪儿"]="Talk about the destination",
        ["先跳过称呼"]="Skip for now",
        ["先逛一段有故事的街区，看看建筑和当地生活；实际街区之后核实。"]="Explore a neighbourhood's stories, architecture and local life. We'll check the actual area later.",
        ["全体全程"]="for the group for the whole trip",
        ["全体每天"]="for the group per day",
        ["其他人数"]="Another party size",
        ["其他城市"]="Another city",
        ["再换个玩法"]="Try another idea",
        ["准备哪天出发？先选个日期，之后还可以改。"]="When would you like to leave? You can change the date later.",
        ["出发地点"]="Starting point",
        ["出发日期"]="Departure date",
        ["刚刚那条习惯记录有变化，我们先按你现在的想法来。"]="That saved habit has changed. Let's go with what you want now.",
        ["别赶啦，我们把重要的留下。第几站先不急着删——这一天最晚几点结束？选个时间，也可以告诉我 HH:mm。"]="Let's keep what matters without rushing. Before we remove any stops, what time does this day need to finish? Pick a time, or tell me in HH:mm.",
        ["原预算先不采用。可以先选每人每天 60 或 120 AUD 作为活动预算，再自由调整；这只是预算档位，不代表已核实的花费。住宿、交通和全部餐饮另计。"]="I'll set the previous amount aside. You can start with 60 or 120 AUD per person per day for activities and adjust it later. These are budget options, not verified costs; accommodation, transport and the full meal budget are separate.",
        ["取消调整"]="Cancel the change",
        ["叫我旅行搭子"]="Call me Travel buddy",
        ["可以先聊聊你想怎样玩，资料也会保留。目前能生成的路线是悉尼，其他城市还需要后续接入。"]="We can explore your ideas and keep the details. Route generation currently supports Sydney; other cities are still to come.",
        ["同行人数收好啦。"]="I've noted how many people are coming.",
        ["同行伙伴"]="Travel companions",
        ["同行需要"]="Companion needs",
        ["和家人"]="With family",
        ["和朋友"]="With friends",
        ["咱们先找一点这趟旅行的灵感。"]="Let's find a little inspiration for this trip. ",
        ["咱们先挑一种玩法：绿意慢走，还是城市故事？你也可以自己说。"]="Shall we start with a nature stroll or the city's stories? You can suggest something else too.",
        ["咱们先留一段可以随心逛逛的时间，再围绕你最想体验的事串起来？"]="Shall we leave some time to wander, then build around the experience you're most looking forward to?",
        ["咱们接着已有的想法准备。这趟先留一段自由探索，还是围绕一个最想体验的事展开？"]="Let's build on your ideas. Shall we leave time to explore, or start with one experience you're looking forward to?",
        ["哼唧，小爪爪抱住地图！还不知道去哪儿，我可没法带路呀～先选一个城市，好不好？🐨"]="Let’s give our map a starting point. Which city are you thinking of? 🐨",
        ["哼唧，小钱包还空着呢！先给我一个大概的活动预算嘛，之后可以改～"]="What’s a comfortable activity budget? A rough amount is fine for now.",
        ["哼，小日历都摊开啦！先挑个出发日期嘛，之后还可以改～"]="Let’s pencil in a departure date. You can always change it later.",
        ["哼，这笔钱是每人还是大家一起、每天还是全程呀？说清楚，我才不会把小钱包算错嘛～"]="Is that per person or for everyone, and per day or for the whole trip?",
        ["喜欢安静"]="Prefer quiet places",
        ["嗨，我叫 Koko，是你的旅行向导。怎么称呼你？也可以先跳过。"]="Hi, I'm Koko, your travel guide. What should I call you? You can skip this too.",
        ["嗨，我叫 Koko，是你的旅行向导。这次想过一段什么样的假期？随便说说就好。"]="Hi, I’m Koko, your travel guide. What kind of getaway are you imagining? Tell me whatever comes to mind.",
        ["回答太长或为空。"]="The answer is empty or too long.",
        ["城市故事"]="City stories",
        ["多逛些，约12000步"]="More exploring, about 12,000 steps",
        ["多逛逛（约15000步）"]="Explore more (about 15,000 steps)",
        ["天数"]="Days",
        ["好呀，只放进这次旅行的小背包。"]="Sure, I’ll use that for this trip only.",
        ["好呀，小馋雷达亮了！我们给好吃的留个位置，预算还是听你的。"]="My food radar is on! Let's make room for something delicious, with the budget still up to you.",
        ["好呀，这个先留个小空位，不耽误我们出发～"]="We can leave that open for now.",
        ["好，先聊这次想去哪儿，习惯有需要再一起改。"]="Let's start with where you'd like to go. We can revisit your habits if we need to.",
        ["好，我们直接聊旅行。你不用先整理好心情，想到什么说什么就好。"]="Let's talk about the trip. You don't need to have everything figured out; say whatever comes to mind.",
        ["好，结束时间沿用原安排，我们不赶着改。"]="We'll keep the original finish time. There's no rush to change it.",
        ["好，这次参考这个习惯。之后临时想换，也随时告诉我。"]="We'll use that habit for this trip. Tell me any time if you feel like changing it.",
        ["如果有合适的展览或文化场所，挑一处慢慢看，再留一段休息；开放时间待核实。"]="If there's a suitable exhibition or cultural venue, choose one to explore at your own pace, then take a break. Opening times still need checking.",
        ["如果目的地有合适的公园、绿道或海边，先留一段轻松散步；具体地点之后核实。"]="If the destination has a suitable park, greenway or waterfront, leave time for an easy stroll. We'll check specific places later.",
        ["对话编号无效。"]="Invalid conversation ID.",
        ["小爪爪停一下！是澳元、人民币还是美元呀？币种不清楚，我会算糊涂的～"]="Which currency is that: AUD, CNY or USD?",
        ["小爪爪先停一下～目前还不能生成这个城市的路线。可以改选悉尼，或者保留这份需求，之后再继续。"]="I can’t generate routes for this city yet. You can choose Sydney or keep this draft for later.",
        ["小爪爪开始数人头啦！一共几个人呀？记得把自己也算进去哦～"]="How many people are coming, including you?",
        ["小背包准备好啦！可以直接点下面的「生成行程」，也可以继续聊。"]="We’re ready to plan! Tap Generate itinerary below, or keep chatting.",
        ["小背包带好啦。你这次想歇一歇、吃点好的，还是找点新鲜感？也可以直接告诉我想去哪儿。"]="My backpack's ready. Are you after a little rest, some good food or something new? You can also tell me where you'd like to go.",
        ["小背包鼓起脸啦！玩几天得先告诉我呀～选 1 到 15 天，我们再慢慢调。"]="How many days should we plan for? Choose 1 to 15 days; we can adjust it later.",
        ["少走些，约6000步"]="Less walking, about 6,000 steps",
        ["就按这个想法试试"]="Let's try this idea",
        ["就选这个方向"]="Choose this direction",
        ["当然可以！你不用每次都一样。告诉我这次想走多少，或者选个大概范围，只改这次就好。"]="Of course; every trip can be different. How much walking would feel right this time? Choose a rough range or tell me your own.",
        ["当然可以，你不用每次都一样。这次想慢一点，还是充实一点？只改这次的节奏。"]="Of course; every trip can be different. Would you like a gentler pace or a fuller day? This changes this trip's pace only.",
        ["必去安排"]="Must-do plans",
        ["悉尼"]="Sydney",
        ["想吃点好的"]="I'd love some good food",
        ["想找点新鲜感"]="Something new sounds good",
        ["想歇一歇"]="I'd like a rest",
        ["想留几天给这次小冒险？"]="How many days do you have for this little adventure?",
        ["慢悠悠（约6000步）"]="Easy strolls (about 6,000 steps)",
        ["我想自己说"]="I'll write my own",
        ["我来写个称呼"]="I'll enter a name",
        ["我自己说时间"]="I'll enter a time",
        ["我还没确定你想几点结束。告诉我例如 14:00，或者先不改时间，原行程都在。"]="What finish time would you like, for example 14:00? You can also keep the original time; your itinerary is still there.",
        ["把用餐和附近的小店串在一起，中间留休息，不为打卡来回赶路。"]="Combine a meal with nearby small shops, with breaks between them and no rushing from one stop to another.",
        ["挑一个餐饮集中的街区边逛边吃，具体店铺、营业和价格之后核实。"]="Explore a neighbourhood with plenty of food options. We'll check specific places, opening times and prices later.",
        ["换一组想法"]="Try another idea",
        ["换个具体方向：「"]="Here's another direction: ",
        ["换个想法吧"]="Try another idea",
        ["换个玩法"]="Try another idea",
        ["换算为每人每日活动预算约 {0} AUD。"]=" That’s about {0} AUD per person per day for activities.",
        ["接下来选出发日期和天数，最多 15 天；没定日期也可以先保留草稿。"]="Next, choose a departure date and trip length, up to 15 days. You can keep the draft if the date is still open.",
        ["收到啦～"]="Noted. ",
        ["收到！预算 {0} {1}（{2}），先收进这次的小背包。"]="Noted: {0} {1}, {2}, for this trip.",
        ["收好啦。"]=" updated.",
        ["改变主意也没关系，旅行又不是交作业。先一起调整想法，确认后再更新行程。"]="Changing your mind is fine; a trip isn't homework. Let's adjust the idea together, then update the itinerary after you confirm.",
        ["改选悉尼"]="Switch to Sydney",
        ["旅行灵感"]="Trip inspiration",
        ["旅行节奏"]="Travel pace",
        ["旅行资料已更新。"]="Your trip details are updated.",
        ["无特别要求"]="No special requirements",
        ["日期"]="Date",
        ["日期先留空，我不会替你确定。草稿会保留，准备生成时再选日期。"]="We'll leave the date open and keep your draft. Choose a date when you're ready to generate the itinerary.",
        ["日期还没定"]="Date still open",
        ["明天"]="Tomorrow",
        ["暂不确定"]="Undecided",
        ["有出发地点或住宿位置了吗？还不知道也可以稍后补充。"]="Do you have a starting point or a place to stay? You can add it later.",
        ["有没有必须避开的安排，或需要特别照顾的出行需求？"]="Is there anything to avoid, or any travel needs I should consider?",
        ["未知的旅行字段。"]="Unknown trip field.",
        ["每人全程"]="per person for the whole trip",
        ["每人每天"]="per person per day",
        ["每天约 "]="about ",
        ["没关系，我们慢慢来。我会留好背包，准备好再告诉我～"]="No rush. Your progress is here whenever you’re ready.",
        ["没有其他要求"]="No other requirements",
        ["沿用这个习惯"]="Use this habit",
        ["活动预算可以选 60 / 120 AUD 每人每天，或自定义金额与范围；住宿、交通和全部餐饮另计。"]="Choose 60 or 120 AUD per person per day for activities, or set your own amount and scope. Accommodation, transport and the full meal budget are separate.",
        ["点选内容与当前提议不一致，请刷新后重试。"]="That selection does not match the current suggestion. Refresh and try again.",
        ["环形码头"]="Circular Quay",
        ["目的地"]="Destination",
        ["目的地待定 · "]="Destination to decide · ",
        ["稍后补充"]="Add later",
        ["绿意慢走"]="Nature stroll",
        ["美食、城市"]="Food and city life",
        ["自定义预算"]="Set my own budget",
        ["自然、风景"]="Nature and scenery",
        ["艺术、文化"]="Art and culture",
        ["范围待确认"]="scope to confirm",
        ["街区逛吃"]="Neighbourhood food stroll",
        ["补充出行资料"]="Add trip details",
        ["补充要求"]="Additional needs",
        ["记好啦："]="Remembered: ",
        ["记好啦：{0}。随时可以在记忆页修改或撤回。"]="Remembered: {0}. You can edit or remove this on the Memories page anytime.",
        ["请输入 1–2000 字，并使用有效的请求编号。"]="Enter 1–2000 characters and use a valid request ID.",
        ["资料会保留。目前只支持悉尼路线，可以改选悉尼或先保存草稿。"]="I'll keep your details. Routes are currently available for Sydney only; switch to Sydney or keep this draft for later.",
        ["资料准备齐啦！可以直接生成行程，也可以继续聊或单独修改。"]="Your trip details are ready. Generate the itinerary, keep chatting or change a detail whenever you like.",
        ["走累了就找个方便休息的地方，下午留给附近的街景，不把一天排满。"]="Take a break when your feet need it, then explore nearby streets in the afternoon. Leave some room in the day.",
        ["还有待确认的信息，可以记住、仅限这次或忽略后再完成。"]="Some preferences still need a decision. Choose Remember, This trip only or Ignore, then finish.",
        ["还有饮食、步行或其他需要照顾的地方吗？没有就跳过吧。"]="Any food, walking or other needs to consider? You can skip this.",
        ["还没想好"]="Not sure yet",
        ["还没确定"]="Not decided yet",
        ["这一天按 "]="I'll work towards a ",
        ["这个玩法怎么样？"]=" What do you think?",
        ["这份心情只放进这次的小背包。"]=" I'll use this only for this trip.",
        ["这条待确认信息已不存在，请刷新对话。"]="This preference candidate no longer exists. Refresh the conversation.",
        ["这次一共几个人？记得包括自己呀。"]="How many people are coming, including you?",
        ["这次充实点"]="A fuller day this trip",
        ["这次和谁一起出发呀？"]="Who’s coming along?",
        ["这次总共几个人出发？包含你自己哦。"]="How many people are travelling, including you?",
        ["这次想去哪个城市？目前可以生成悉尼路线，也能先保存其他城市的想法。"]="Which city would you like to visit? I can plan Sydney routes, or save ideas for other cities for later.",
        ["这次想去哪里呀？目前可以生成悉尼路线；其他城市可以先保存想法。"]="Where would you like to go? I can plan Sydney routes now, or save ideas for another city.",
        ["这次想慢悠悠，还是多逛几个地方？"]="Would you rather take it slow or fit in a few more places?",
        ["这次想换换"]="Change it for this trip",
        ["这次想玩什么"]="Trip interests",
        ["这次按每人每天 "]="For this trip, I'll use ",
        ["这次最想发现什么？也可以直接告诉我好几个喜好。"]="What would you love to discover? You can mention a few interests.",
        ["这次活动预算大概多少？请一起告诉我币种，以及是每人还是全体、每天还是全程。"]="What’s your activity budget? Include the currency, whether it’s per person or for everyone, and per day or for the whole trip.",
        ["这次特别想去哪里，或者想做什么？没有也可以先跳过。"]="Is there a place or experience you really want to include? It’s fine to skip this.",
        ["这次的小步数收好啦。长期习惯不动，我们按这次舒服的范围来。"]="I've noted the walking range for this trip. Your long-term habit stays the same; we'll use what feels comfortable this time.",
        ["这次的旅行资料先收好啦。"]="I've kept your trip details.",
        ["这次的旅行资料已更新，可以继续调整草案。"]="Your trip details are updated. We can keep refining the draft.",
        ["这次的节奏收好啦，原来的长期习惯保留。之后想再改，也没关系。"]="I've noted this trip's pace and kept your long-term habit. You can change this trip again whenever you like.",
        ["这次轻松点"]="A gentler pace this trip",
        ["这段对话已清空，请开启新对话。"]="This conversation has been cleared. Start a new one.",
        ["这笔活动预算使用什么币种？目前支持澳元 AUD；其他币种需要先确认澳元金额。"]="Which currency is your activity budget in? Planning currently uses AUD; for another currency, we’ll need you to confirm an AUD amount.",
        ["这笔金额是每人还是全体、每天还是全程呢？"]="Is this amount per person or for everyone, and per day or for the whole trip?",
        ["这笔预算先按原币种保留。生成路线前还需要一份澳元活动预算。"]="I’ll keep this budget in its original currency. Before generating a route, we’ll also need an activity budget in AUD.",
        ["这组选项已更新，请使用当前回复，或直接告诉 Koko。 "]="These options have changed. Choose a current option, or tell Koko directly.",
        ["这项偏好有多条旧记录，请先在记忆页整理，再确认更新。"]="This preference has several previous records. Review them on the Memories page before confirming an update.",
        ["适中（约10000步）"]="Moderate (about 10,000 steps)",
        ["选悉尼"]="Choose Sydney",
        ["选日期和天数"]="Choose date and length",
        ["避免长时间步行"]="Avoid long walks",
        ["避开博物馆，把另一段时间留给街景、小店和休息。"]="Skip museums and leave that time for street scenes, small shops and a rest.",
        ["避开安排"]="Things to avoid",
        ["那就去悉尼，其他资料都留着。"]="Sydney it is. I'll keep the rest of your trip details.",
        ["那就带一点好奇心出发，给没见过的小惊喜留点空位。"]="Let's take a little curiosity along and leave space for a few new discoveries.",
        ["那这次不用努力打卡啦。我们给休息和发呆留点位置。"]="There's no need to tick everything off. Let's leave room to rest and do very little.",
        ["那这次不用努力打卡啦。我们给坐着吹风和发呆留点位置。"]="There's no need to tick everything off. Let's leave room to sit, enjoy the breeze and do very little.",
        ["那这次就按「"]="Let's plan around ",
        ["长期偏好刚有变化，请刷新对话，查看旧值后再确认替换。"]="Your saved preference has changed. Refresh the conversation and review the old value before confirming a replacement.",
        ["长期偏好检索暂时不可用，先按这次对话继续准备。"]="Long-term preference retrieval is temporarily unavailable. We can continue with this conversation.",
        ["长期偏好检索暂时不可用，本轮参考已确认的本机记录。"]="Long-term preference retrieval is temporarily unavailable. I’ll use the confirmed records stored locally for this turn.",
        ["需要多休息"]="More rest breaks",
        ["预算"]="Budget",
        ["预算先收好啦，我们再确认金额、币种和范围。"]="I’ve noted the budget. Let’s confirm the amount, currency and scope.",
        ["预算金额"]="Budget amount",
    };
    private sealed record Template(Regex Pattern,string Translation,string[] Tokens);
    private static readonly Regex Token=new(@"\{([^{}]+)\}");
    private static readonly Template[] Templates=Copy.Where(x=>Token.IsMatch(x.Key)).Select(x=> {
        var matches=Token.Matches(x.Key);var pattern="\\A";var cursor=0;var tokens=new List<string>();
        foreach(Match match in matches) {
            pattern+=Regex.Escape(x.Key[cursor..match.Index])+"(.+?)";
            cursor=match.Index+match.Length;tokens.Add(match.Value);
        }
        pattern+=Regex.Escape(x.Key[cursor..])+"\\z";
        return new Template(new Regex(pattern,RegexOptions.Singleline),x.Value,tokens.ToArray());
    }).ToArray();
    private static readonly KeyValuePair<string,string>[] Prefixes=Copy
        .Where(x=>x.Key.Length>=8 && !Token.IsMatch(x.Key)).OrderByDescending(x=>x.Key.Length).ToArray();

    internal sealed record Display(List<PetMessage> Messages,ProfilingDialogue? Dialogue,string MemoryWarning);
    internal static Display Presentation(PetSession session) {
        var messages=session.Messages.Select(message=>message.Role!="assistant" ? message:message with {
            Text=Translate(message.Text),QuickReplies=message.QuickReplies?.Select(option=>option with {Text=Translate(option.Text)}).ToArray()
        }).ToList();
        var dialogue=session.Dialogue is null ? null:JsonSerializer.Deserialize<ProfilingDialogue>(JsonSerializer.Serialize(session.Dialogue,JsonExtensions.Options),JsonExtensions.Options);
        if(dialogue is not null) {
            dialogue.Ideas=dialogue.Ideas.Select(x=>Translate(x)).ToArray();
            if(dialogue.Inspiration is {} inspiration)dialogue.Inspiration=inspiration with {Title=Translate(inspiration.Title),Ideas=inspiration.Ideas.Select(x=>Translate(x)).ToArray()};
        }
        return new(messages,dialogue,Translate(session.MemoryWarning));
    }
    private static string DisplayValue(string value) {
        var known=value switch {
            "轻松随意" or "轻松" or "慢悠悠"=>"Relaxed", "适中"=>"Moderate", "紧凑充实" or "紧凑"=>"Busy",
            "低精力"=>"Low energy", "中等精力"=>"Moderate energy", "高精力"=>"High energy",
            "美食"=>"Food", "自然"=>"Nature", "风景"=>"Scenery", "文化"=>"Culture", "艺术"=>"Art", "购物"=>"Shopping", "城市"=>"City life",
            "无"=>"None", "无特别要求"=>"No special requirements", _=>value
        };
        if(known!=value)return known;
        var steps=Regex.Match(value,@"^约([0-9]+)步$");
        if(steps.Success)return "About "+steps.Groups[1].Value+" steps";
        return value;
    }
    internal static string Translate(string value,int depth=0)
    {
        if(value.Length==0 || depth>16)return value;
        if(Copy.TryGetValue(value,out var exact))return exact;
        foreach(var template in Templates) {
            var match=template.Pattern.Match(value);if(!match.Success)continue;
            var arguments=template.Tokens.Select((token,index)=> {
                var argument=match.Groups[index+1].Value;
                // Only known application labels/ideas are translated inside a template.
                // Names, numeric facts, currencies and remembered user prose remain exact.
                if(token is "{labels}" or "{direction}" or "{idea}" or "{habit}" or "{basis}" or "{2}" or "{question}")
                    argument=DisplayValue(Translate(argument,depth+1));
                return (token,value:argument);
            }).ToDictionary(x=>x.token,x=>x.value);
            return Token.Replace(template.Translation,m=>arguments.GetValueOrDefault(m.Value,m.Value));
        }
        foreach(var separator in new[]{"\n"," · ","、"}) {
            if(!value.Contains(separator,StringComparison.Ordinal))continue;
            var joiner=separator=="、" ? ", ":separator;
            var parts=value.Split(separator);var translated=parts.Select(part=>Translate(part,depth+1)).ToArray();
            return parts.SequenceEqual(translated) ? value:string.Join(joiner,translated);
        }
        foreach(var fragment in Prefixes) {
            if(value.Length>fragment.Key.Length && value.StartsWith(fragment.Key,StringComparison.Ordinal))
                return fragment.Value+Translate(value[fragment.Key.Length..],depth+1);
        }
        return value;
    }
}
