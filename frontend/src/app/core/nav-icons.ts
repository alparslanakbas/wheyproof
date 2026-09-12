// Icon paths shared by the nav dropdowns (Categories, Calculators) and the
// calculator index cards, so the icon set stays consistent across
// site-header.ts, deals-list.ts and calculator-list-page.ts. Keys must match
// the category slugs (see category-labels.ts) and calculator slugs.
export const CATEGORY_ICON_PATHS: Record<string, string> = {
  'protein-powder': 'M6 4h12M7.5 4v4L4 19a1.5 1.5 0 0 0 1.4 2h13.2a1.5 1.5 0 0 0 1.4-2L16.5 8V4',
  creatine: 'M13 2 4 14h6l-1 8 9-12h-6l1-8Z',
  'amino-acids': 'M9 3h6M10 3v5.5L4.5 18a2 2 0 0 0 1.8 3h11.4a2 2 0 0 0 1.8-3L14 8.5V3',
  'pre-workout': 'M3 12h4l2-7 4 14 2-7h6',
  hydration: 'M12 3s6 7 6 11.5A6 6 0 0 1 6 14.5C6 10 12 3 12 3Z',
  'fat-burners': 'M12 3c-1 3-4 4-4 8a4 4 0 0 0 8 0c0-1-.5-1.5-1-2 .3 1.5-.5 2.5-1.5 2.5-1.4 0-2-1.2-1.2-2.7C13 7 12.6 5 12 3Z',
  'mass-gainers': 'M3 17l6-6 4 4 8-8M14 7h7v7',
  vitamins: 'M12 2l2.9 6.3 6.9.6-5.2 4.6 1.6 6.8L12 16.9l-6.2 3.4 1.6-6.8L3.2 8.9l6.9-.6Z',
  'protein-snacks': 'M12 21s-7-4.4-9.5-9A5.5 5.5 0 0 1 12 6a5.5 5.5 0 0 1 9.5 6C19 16.6 12 21 12 21Z',
};
export const DEFAULT_CATEGORY_ICON = 'M4 6h16M4 12h16M4 18h16';

// Phosphor font icons for the home page category strip and the header
// dropdown. Kept apart from the path list: the new UI uses font icons, the
// older calculator cards use paths.
export const CATEGORY_PHOSPHOR_ICONS: Record<string, string> = {
  'protein-powder': 'ph-jar',
  'amino-acids': 'ph-share-network',
  creatine: 'ph-lightning',
  'pre-workout': 'ph-gauge',
  hydration: 'ph-drop',
  'fat-burners': 'ph-fire',
  'mass-gainers': 'ph-barbell',
  vitamins: 'ph-shield-plus',
  'protein-snacks': 'ph-cookie',
};

export const DEFAULT_CATEGORY_PHOSPHOR_ICON = 'ph-dots-three';

export function categoryPhosphorIcon(slug: string): string {
  return CATEGORY_PHOSPHOR_ICONS[slug] ?? DEFAULT_CATEGORY_PHOSPHOR_ICON;
}

export type CalculatorIcon = 'plate' | 'capsule' | 'flame' | 'ruler' | 'droplet';

export const CALCULATOR_ICON_PATHS: Record<CalculatorIcon, string> = {
  plate: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18ZM12 8v4l3 2',
  capsule: 'M6.5 6.5 17.5 17.5M8.6 4.4a5 5 0 0 1 7 7l-4.2 4.2a5 5 0 0 1-7-7Z',
  flame: 'M12 3c-1 3-4 4-4 8a4 4 0 0 0 8 0c0-1-.5-1.5-1-2 .3 1.5-.5 2.5-1.5 2.5-1.4 0-2-1.2-1.2-2.7C13 7 12.6 5 12 3Z',
  ruler: 'M4 15 15 4l5 5-11 11ZM8 11l2 2M11 8l2 2M14 5l2 2',
  droplet: 'M12 3s6 7 6 11.5A6 6 0 0 1 6 14.5C6 10 12 3 12 3Z',
};

// Body calculators by slug; every supplement dosage calculator uses 'capsule'.
const BODY_CALCULATOR_ICON_BY_SLUG: Record<string, CalculatorIcon> = {
  'calorie-needs': 'flame',
  bmi: 'ruler',
  'water-intake': 'droplet',
};

// Icon for "/calculators/{slug}": protein needs and body calculators have
// their own; supplement dosage calculators (creatine, beta-alanine...) share
// 'capsule'.
export function calculatorIconPath(slug: string): string {
  if (slug === 'protein') return CALCULATOR_ICON_PATHS.plate;
  const bodyIcon = BODY_CALCULATOR_ICON_BY_SLUG[slug];
  return CALCULATOR_ICON_PATHS[bodyIcon ?? 'capsule'];
}

// Each tool shows what it does at a glance; nutrition, energy, measurement,
// water and supplements don't share one calculator symbol.
const CALCULATOR_PHOSPHOR_ICON_BY_SLUG: Record<string, string> = {
  protein: 'ph-bowl-food',
  'calorie-needs': 'ph-fire',
  bmi: 'ph-scales',
  'water-intake': 'ph-drop',
  'creatine-dosage': 'ph-lightning',
  'beta-alanine-dosage': 'ph-waves',
  'citrulline-dosage': 'ph-heartbeat',
  'betaine-dosage': 'ph-drop-half-bottom',
  'eaa-dosage': 'ph-share-network',
};

export function calculatorPhosphorIcon(slug: string): string {
  return CALCULATOR_PHOSPHOR_ICON_BY_SLUG[slug] ?? 'ph-pill';
}
