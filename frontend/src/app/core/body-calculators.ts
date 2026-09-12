import { MARKET } from './market';
import { SITE_NAME } from './site-identity';

export type BodyField = 'gender' | 'age' | 'height' | 'weight' | 'activity';

/**
 * Calculator input, always METRIC (cm, kg): the formulas are defined that
 * way. The page converts from the units the visitor typed (ft/in, lb).
 */
export interface BodyCalcInput {
  gender: 'male' | 'female';
  age: number | null;
  height: number | null;
  weight: number | null;
  activityId: string;
}

export interface BodyCalcResult {
  // The main result, shown large
  primaryValue: string;
  primaryUnit: string;
  primaryLabel: string;
  // Extra rows under the main result (calories by goal, BMI class, ...)
  details: { label: string; value: string }[];
  note?: string;
}

export interface ActivityOption {
  id: string;
  label: string;
  description: string;
  factor: number;
}

// Activity multipliers commonly used with Mifflin-St Jeor.
export const ACTIVITY_OPTIONS: ActivityOption[] = [
  { id: 'sedentary', label: 'Sedentary', description: 'Desk job, no exercise', factor: 1.2 },
  { id: 'light', label: 'Lightly active', description: '1-3 days a week', factor: 1.375 },
  { id: 'moderate', label: 'Moderately active', description: '3-5 days a week', factor: 1.55 },
  { id: 'active', label: 'Very active', description: '6-7 days a week', factor: 1.725 },
  { id: 'athlete', label: 'Athlete', description: 'Twice a day or heavy physical work', factor: 1.9 },
];

export interface BodyCalculator {
  slug: string;
  name: string;
  title: string;
  description: string;
  h1: string;
  intro: string;
  fields: BodyField[];
  disclaimer: string;
  calculate: (input: BodyCalcInput) => BodyCalcResult | null;
}

const KG_TO_LB = 2.20462262;
const ML_PER_FL_OZ = 29.5735296;
const ML_PER_CUP = 236.588237;

const whole = (value: number) => Math.round(value).toLocaleString(MARKET.locale);

function activityFactor(id: string): number {
  return ACTIVITY_OPTIONS.find((a) => a.id === id)?.factor ?? 1.375;
}

export const BODY_CALCULATORS: BodyCalculator[] = [
  {
    slug: 'calorie-needs',
    name: 'Daily Calorie Needs',
    title: `Daily Calorie Calculator (TDEE) | ${SITE_NAME}`,
    description:
      'Calculate your daily calorie needs (TDEE) from your height, weight, age and activity level, and the calories to lose or gain weight.',
    h1: 'Daily Calorie Calculator',
    intro:
      'Uses the Mifflin-St Jeor equation to estimate your basal metabolic rate and your total daily calorie needs for your activity level. The result is an estimate; real needs vary from person to person.',
    fields: ['gender', 'age', 'height', 'weight', 'activity'],
    disclaimer:
      'This is an estimate based on a widely used equation, not a personal nutrition plan. Real needs vary with muscle mass, hormones and health history; for a precise plan, talk to a registered dietitian.',
    calculate: (input) => {
      const { gender, age, height, weight } = input;
      if (!age || !height || !weight) return null;
      if (age < 10 || age > 100 || height < 100 || height > 250 || weight < 30 || weight > 300) return null;

      // Mifflin-St Jeor: basal metabolic rate (BMR)
      const bmr = 10 * weight + 6.25 * height - 5 * age + (gender === 'male' ? 5 : -161);
      const tdee = Math.round(bmr * activityFactor(input.activityId));

      return {
        primaryValue: whole(tdee),
        primaryUnit: 'kcal / day',
        primaryLabel: 'To maintain your weight',
        details: [
          { label: 'Basal metabolic rate (BMR)', value: `${whole(bmr)} kcal` },
          { label: 'To lose weight (~500 kcal deficit)', value: `${whole(tdee - 500)} kcal` },
          { label: 'To gain weight (~500 kcal surplus)', value: `${whole(tdee + 500)} kcal` },
        ],
        note:
          'A 500 kcal daily deficit or surplus is a common starting point for about 1 lb of change per week. Very large deficits raise the risk of losing muscle.',
      };
    },
  },
  {
    slug: 'bmi',
    name: 'Body Mass Index (BMI)',
    title: `BMI Calculator | ${SITE_NAME}`,
    description:
      'Calculate your body mass index (BMI) from your height and weight, and learn why BMI can mislead for people who lift.',
    h1: 'BMI Calculator',
    intro:
      'BMI is a simple ratio of height and weight. It gives a rough indicator for the general population but can\'t tell muscle from fat, so it can mislead for people who train regularly.',
    fields: ['height', 'weight'],
    disclaimer:
      'BMI ignores muscle mass, body fat and where fat is carried. A muscular athlete can land in the "overweight" range, which alone is not a sign of a health problem. For a health assessment, talk to a doctor.',
    calculate: (input) => {
      const { height, weight } = input;
      if (!height || !weight) return null;
      if (height < 100 || height > 250 || weight < 30 || weight > 300) return null;

      const meters = height / 100;
      const bmi = weight / (meters * meters);

      // Standard adult ranges used by the World Health Organization and CDC.
      const category =
        bmi < 18.5 ? 'Underweight' : bmi < 25 ? 'Healthy weight' : bmi < 30 ? 'Overweight' : 'Obesity';

      const idealMinLb = whole(18.5 * meters * meters * KG_TO_LB);
      const idealMaxLb = whole(24.9 * meters * meters * KG_TO_LB);

      return {
        primaryValue: bmi.toFixed(1),
        primaryUnit: '',
        primaryLabel: category,
        details: [
          { label: 'Healthy range for your height (BMI 18.5-24.9)', value: `${idealMinLb}-${idealMaxLb} lb` },
        ],
        note:
          'BMI reads high for people with a lot of muscle. A body fat measurement says more about body composition.',
      };
    },
  },
  {
    slug: 'water-intake',
    name: 'Daily Water Intake',
    title: `Daily Water Intake Calculator | ${SITE_NAME}`,
    description: 'Calculate how much water you need a day from your weight and activity level.',
    h1: 'Daily Water Intake Calculator',
    intro:
      'How much water you need depends on your weight and activity. The calculation below uses a commonly cited range; hot weather and heavy sweating raise it.',
    fields: ['weight', 'activity'],
    disclaimer:
      'This is a general reference. If you have a condition that requires limiting fluids, such as kidney or heart disease, follow your doctor\'s advice.',
    calculate: (input) => {
      const { weight } = input;
      if (!weight || weight < 30 || weight > 300) return null;

      // Common reference: 30-35 ml per kg. More activity adds fluid for sweat.
      const factor = activityFactor(input.activityId);
      const extraMl = Math.round((factor - 1.2) * 1000);
      const minMl = weight * 30 + extraMl;
      const maxMl = weight * 35 + extraMl;

      return {
        primaryValue: `${whole(minMl / ML_PER_FL_OZ)}–${whole(maxMl / ML_PER_FL_OZ)}`,
        primaryUnit: 'fl oz / day',
        primaryLabel: 'Your daily water needs',
        details: [
          { label: 'In cups (8 fl oz)', value: `${Math.round(minMl / ML_PER_CUP)}-${Math.round(maxMl / ML_PER_CUP)} cups` },
          { label: 'In liters', value: `${(minMl / 1000).toFixed(1)}–${(maxMl / 1000).toFixed(1)} L` },
        ],
        note:
          'Paying extra attention to water intake while taking creatine is a common recommendation. Tea, coffee and food also count toward your total.',
      };
    },
  },
];

export function findBodyCalculator(slug: string): BodyCalculator | undefined {
  return BODY_CALCULATORS.find((c) => c.slug === slug);
}
