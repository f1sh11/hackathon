import type { Profile } from '../../types/contracts';
import { Icon } from '../../components/Icon';
import { TravelBuddy as Buddy } from '../../components/TravelBuddy';
import { Radar } from './Radar';

export function ProfileCard({ profile, onCreateTask }: { profile: Profile; onCreateTask: () => void }) {
  return <>
          <div className="intro"><div className="eyebrow">角色图鉴</div><h1>你的旅行角色登场！</h1><p className="subtitle">没有高低分，只有属于你的旅行节奏。</p></div>
          <section className="profile-visual"><div className="traveler-card-top"><span><Icon name="star" size={13} /> 旅行角色卡</span><span>NO. {profile.id.slice(0, 6).toUpperCase()}</span></div><div className="profile-title"><span className="profile-emblem"><Buddy size={80} excited /></span><div><div className="profile-name">{profile.character}</div><p>{profile.values[2] < 40 ? '慢一点，遇见更多。' : '自在探索，也照顾自己的节奏。'}</p></div></div><Radar profile={profile} /><div className="readonly-badge"><Icon name="lock" size={12} /> 问答生成 · 画像已固定</div></section>
          <section className="profile-traits">{[['节奏', profile.values[2] < 40 ? '悠闲留白' : profile.values[2] > 70 ? '丰富紧凑' : '松紧适中'], ['步行', profile.values[0] < 40 ? '短途优先' : profile.values[0] > 70 ? '喜欢步行' : '边走边停'], ['路线', profile.values[1] < 40 ? '尽量平缓' : profile.values[1] > 70 ? '愿意登高' : '少量坡道'], ['探索', profile.values[4] > 70 ? '小众发现' : profile.values[4] < 40 ? '经典优先' : '顺路探索']].map(([label, text]) => <div key={label}><span className="small">{label}</span><strong>{text}</strong></div>)}</section>
          <div className="soft-callout"><Icon name="heart" size={19} /><p>画像是你的旅行性格。<br /><span>今天的精力、预算和时间，可以在任务里单独填写。</span></p></div><button className="button primary full-button" onClick={() => onCreateTask()}>带着这个角色，出发<Icon name="arrow" size={18} /></button>
        </>;
}
