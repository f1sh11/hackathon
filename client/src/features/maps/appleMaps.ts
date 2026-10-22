export type MapMode = 'walking' | 'driving' | 'transit';

export interface PlaceReference {
  id: string;
  label: string;
  query: string;
  kind: 'landmark' | 'search' | 'return' | 'unknown';
  note: string;
}

const landmark = (id: string, label: string, query: string): PlaceReference => ({
  id, label, query, kind: 'landmark', note: '示例地点参考，出发前请在 Apple Maps 中核对位置与开放情况。',
});
const search = (id: string, label: string, query: string): PlaceReference => ({
  id, label, query, kind: 'search', note: '这是泛化的示例活动，尚未确定地址；请先搜索并确认具体地点。',
});

// The planner uses illustrative activities. These references provide search terms,
// never a claim that a sample activity has been geocoded or matched to a Place ID.
export const PLACE_REFERENCES: Readonly<Record<string, PlaceReference>> = Object.freeze({
  harbour: landmark('harbour', '环形码头', 'Circular Quay, Sydney NSW, Australia'),
  garden: landmark('garden', '悉尼皇家植物园', 'Royal Botanic Garden Sydney, NSW, Australia'),
  rocks: landmark('rocks', '岩石区', 'The Rocks, Sydney NSW, Australia'),
  gallery: landmark('gallery', '新南威尔士州美术馆', 'Art Gallery of New South Wales, Sydney, Australia'),
  museum: landmark('museum', '澳大利亚博物馆', 'Australian Museum, Sydney NSW, Australia'),
  library: landmark('library', '新南威尔士州立图书馆', 'State Library of NSW, Sydney, Australia'),
  waterfront: landmark('waterfront', 'Barangaroo 滨水公园', 'Barangaroo Reserve, Sydney NSW, Australia'),
  park: landmark('park', '悉尼海德公园', 'Hyde Park, Sydney NSW, Australia'),
  lookout: landmark('lookout', '天文台山', 'Observatory Hill, Sydney NSW, Australia'),
  studio: search('studio', '搜索手作工作室', 'art studios in Sydney NSW Australia'),
  market: search('market', '搜索悉尼市集', 'markets in Sydney NSW Australia'),
  reserve: search('reserve', '搜索自然保留区', 'nature reserves in Sydney NSW Australia'),
  lunch: search('lunch', '搜索简餐与咖啡', 'cafes and lunch in Sydney NSW Australia'),
  picnic: search('picnic', '搜索野餐地点', 'picnic areas in Sydney NSW Australia'),
  coffee: search('coffee', '搜索咖啡店', 'coffee in Sydney NSW Australia'),
  unverified: {
    id: 'unverified', label: '待核实的特别展览', query: '', kind: 'unknown',
    note: '这个示例地点没有可确认的地址，暂不提供导航。',
  },
  return: {
    id: 'return', label: '返回出发点', query: '', kind: 'return',
    note: '请填写自己的出发或返程地址，不会自动指定酒店或读取定位。',
  },
});

export function getPlaceReference(id: string): PlaceReference {
  if (Object.hasOwn(PLACE_REFERENCES, id)) return PLACE_REFERENCES[id];
  return { id, label: '待确认地点', query: '', kind: 'unknown', note: '尚未建立地点参考，请先确认具体地址。' };
}

export function appleMapsSearchUrl(query: string): string | null {
  const text = query.trim();
  if (!text) return null;
  const url = new URL('https://maps.apple.com/search');
  url.searchParams.set('query', text);
  return url.href;
}

export function appleMapsDirectionsUrl(destination: string, mode: MapMode, source?: string): string | null {
  const text = destination.trim();
  if (!text || !['walking', 'driving', 'transit'].includes(mode)) return null;
  const url = new URL('https://maps.apple.com/directions');
  url.searchParams.set('destination', text);
  url.searchParams.set('mode', mode);
  if (source?.trim()) url.searchParams.set('source', source.trim());
  return url.href;
}

export function appleMapsPlaceActions(id: string, mode: MapMode, returnAddress = ''): {
  reference: PlaceReference;
  searchUrl: string | null;
  directionsUrl: string | null;
} {
  const reference = getPlaceReference(id);
  const query = reference.kind === 'return' ? returnAddress.trim() : reference.query;
  const mayNavigate = reference.kind === 'landmark' || reference.kind === 'return';
  return {
    reference,
    searchUrl: appleMapsSearchUrl(query),
    directionsUrl: mayNavigate ? appleMapsDirectionsUrl(query, mode) : null,
  };
}
