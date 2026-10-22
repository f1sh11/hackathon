export const shortTime = (v: string) => v.slice(0, 5);
export const minuteOfDay = (v: string) => Number(v.slice(0, 2)) * 60 + Number(v.slice(3, 5));
export const currency = (n: number) => new Intl.NumberFormat('en-AU', { maximumFractionDigits: 2 }).format(n);
export const dateLabel = (v: string) => new Intl.DateTimeFormat('zh-CN', { month: 'long', day: 'numeric' }).format(new Date(v + 'T12:00:00'));
export const today = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'Australia/Sydney', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
export const reasonLabel = (reason: string) => reason.includes('轻松版') ? '更轻松的安排' : reason.includes('备注') ? '你补充的要求' : /预算|支出/.test(reason) ? '预算与余力' : /平缓|坡度|爬坡/.test(reason) ? '适合你的路线' : /休息|简餐/.test(reason) ? '休息也在计划里' : '照顾你的节奏';
export const statusLabel = (s: string) => (({ completed: '已生成', infeasible: '需要调整', failed: '生成失败' } as Record<string, string>)[s] ?? s);
export const themeLabel = (s: string) => s === 'harbour' ? '海港与城市' : s === 'culture' ? '文化与咖啡' : '公园与自然';
