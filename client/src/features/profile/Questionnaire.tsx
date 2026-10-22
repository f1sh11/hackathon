import { Icon } from '../../components/Icon';
import { TravelBuddy as Buddy } from '../../components/TravelBuddy';
import { questions } from './questions';

type QuestionnaireProps = {
  answers: (number | null)[];
  step: number;
  busy: boolean;
  onAnswer: (answer: number) => void;
  onPrevious: () => void;
  onNext: () => void;
  onFinish: () => void;
};

export function Questionnaire({ answers, step, busy, onAnswer, onPrevious, onNext, onFinish }: QuestionnaireProps) {
  return <>
          <div className="intro onboarding-intro"><div className="eyebrow">认识你的旅行角色</div><h1>嗨，冒险家！</h1><p className="subtitle">我是漫漫，先用 6 个小问题认识你。</p></div>
          <section className="questionbox" aria-label="创建旅行画像">
            <div className="question-progress"><span>角色创建</span><strong className="num">{step + 1}<span> / 6</span></strong></div>
            <div className="progress-rail" role="progressbar" aria-label="答题进度" aria-valuemin={0} aria-valuemax={6} aria-valuenow={step + 1}><span style={{ width: ((step + 1) / 6 * 100) + '%' }} /></div>
            <div className="buddy-question"><Buddy size={106} /><div className="speech-bubble"><h2 className="question-title">{questions[step].title}</h2></div></div>
            <p className="subtitle question-hint">{questions[step].hint}</p>
            <div className="choices">{questions[step].options.map((o, i) => <button key={o} type="button" className="choice" aria-pressed={answers[step] === i} onClick={() => onAnswer(i)} disabled={busy}><span className="choice-letter">{['A', 'B', 'C'][i]}</span><span>{o}</span><span className="choice-check">{answers[step] === i && <Icon name="check" size={17} />}</span></button>)}</div>
            <div className="question-footer"><button className="button quiet" onClick={onPrevious} disabled={step === 0 || busy}><Icon name="back" size={17} />上一题</button><button className="button primary" disabled={answers[step] === null || busy} onClick={() => step === 5 ? onFinish() : onNext()}>{busy ? '正在保存…' : step === 5 ? '生成我的角色' : '继续'}<Icon name="arrow" size={19} /></button></div>
          </section><p className="privacy-note"><Icon name="lock" size={13} />问答生成固定画像，雷达不能手动调分</p>
        </>;
}
