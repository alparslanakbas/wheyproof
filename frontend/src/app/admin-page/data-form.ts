// The admin product data editor's form and its unsaved inputs. A click outside
// the editor closes it, and reopening rebuilt the form from the saved values:
// half-entered input (say, protein and carbs only) was silently lost.

import { AdminProduct, ManualNutrition } from './admin.service';
import { NutritionRowForm, storedOtherRows } from './nutrition-rows';

export type NutritionField = 'serving' | 'calories' | 'protein' | 'carbs' | 'fat' | 'fiber';

/** The admin editor's working copy; inputs are kept as text until saved. */
export interface ProductDataForm extends Record<NutritionField, string> {
  product: AdminProduct;
  /** '' = automatic. */
  category: string;
  /** Label rows beyond the macros (Supplement Facts). */
  otherRows: NutritionRowForm[];
}

/** Category and nutrition are saved by separate buttons, as separate requests. */
export type SavedData = 'category' | 'nutrition';

/** Units a serving may be counted in: the backend's list, in the same spelling. */
export const SERVING_UNITS = ['capsule', 'tablet', 'softgel', 'sachet'] as const;

// The words labels print for a counted serving ("1 Cap", "1 sachet x 7.5 gr").
const SERVING_WORDS: Record<string, (typeof SERVING_UNITS)[number]> = {
  cap: 'capsule',
  caps: 'capsule',
  capsule: 'capsule',
  capsules: 'capsule',
  tab: 'tablet',
  tabs: 'tablet',
  tablet: 'tablet',
  tablets: 'tablet',
  softgel: 'softgel',
  softgels: 'softgel',
  sachet: 'sachet',
  sachets: 'sachet',
};
// "7,5g" and "7.5 gr" are both typed (2026-09-28: "7,5g" was refused while the box
// was a number field no more; a Turkish browser used to turn the comma into a point).
const GRAMS = String.raw`\s*(?:g|gr|grams?)`;
const NUMBER = String.raw`(\d+(?:[.,]\d+)?)`;
const IN_GRAMS = String.raw`(?:\(\s*${NUMBER}${GRAMS}\s*\))?`;
const GRAM_SERVING = new RegExp(`^${NUMBER}(?:${GRAMS})?$`, 'i');
const ML_SERVING = new RegExp(String.raw`^${NUMBER}\s*ml\s*${IN_GRAMS}$`, 'i');
const COUNTED_SERVING = new RegExp(String.raw`^(\d+)\s*([a-z]+)\s*${IN_GRAMS}$`, 'i');
const decimal = (text: string) => Number(text.replace(',', '.'));

type Serving = Pick<
  ManualNutrition,
  'servingSizeGrams' | 'servingCount' | 'servingUnit' | 'servingMilliliters'
>;
const NO_SERVING: Serving = {
  servingSizeGrams: null,
  servingCount: null,
  servingUnit: null,
  servingMilliliters: null,
};

/**
 * The serving box: grams ("7.5", "7,5 g"), ml for a liquid ("20 ml"), or a count
 * as labels print it ("1 capsule", "1 sachet (7.5 g)"), whose weight is often not
 * on the label. Null when it is none of these.
 */
export function parseServing(text: string): Serving | null {
  const t = text.trim();
  if (t === '') return { ...NO_SERVING };
  const grams = t.match(GRAM_SERVING);
  if (grams) return { ...NO_SERVING, servingSizeGrams: decimal(grams[1]) };
  const liquid = t.match(ML_SERVING);
  if (liquid) {
    return {
      ...NO_SERVING,
      servingSizeGrams: liquid[2] ? decimal(liquid[2]) : null,
      servingMilliliters: decimal(liquid[1]),
    };
  }
  const counted = t.match(COUNTED_SERVING);
  const unit = counted && SERVING_WORDS[counted[2].toLowerCase()];
  if (!counted || !unit) return null;
  return {
    ...NO_SERVING,
    servingSizeGrams: counted[3] ? decimal(counted[3]) : null,
    servingCount: Number(counted[1]),
    servingUnit: unit,
  };
}

function parseNutrition(json: string | null): Record<string, string> {
  if (!json) return {};
  try {
    return JSON.parse(json) as Record<string, string>;
  } catch {
    return {};
  }
}

// A counted or liquid serving ("2 capsules", "20 ml") is only in the table;
// grams are also a column.
function storedServing(product: AdminProduct, table: Record<string, string>): string {
  const row = table['Serving Size']?.trim() ?? '';
  const parsed = parseServing(row);
  if (parsed?.servingUnit || parsed?.servingMilliliters) return row;
  return product.servingSizeGrams?.toString() ?? row.match(/\d+(?:\.\d+)?/)?.[0] ?? '';
}

/**
 * The form built from the product's saved data. An automatic reading can hold
 * rows this editor can't express ("10%"); they come back by name.
 */
export function storedDataForm(product: AdminProduct): {
  form: ProductDataForm;
  skipped: string[];
} {
  const table = parseNutrition(product.nutritionJson);
  // "160", "2.5g" -> the number as text for the input.
  const value = (label: string) => table[label]?.match(/\d+(?:\.\d+)?/)?.[0] ?? '';
  const other = storedOtherRows(table);
  return {
    form: {
      product,
      category: product.categoryIsManual ? (product.category ?? '') : '',
      serving: storedServing(product, table),
      calories: value('Calories'),
      protein: value('Protein'),
      carbs: value('Total Carbohydrate'),
      fat: value('Total Fat'),
      fiber: value('Dietary Fiber'),
      otherRows: other.rows,
    },
    skipped: other.skipped,
  };
}

/** The save request built from the form, or what to tell the admin instead. */
export function nutritionRequest(
  form: ProductDataForm,
): { body: ManualNutrition } | { error: string } {
  const serving = parseServing(form.serving);
  // Says what was typed and gives examples; a bare example number ("30") read as
  // a minimum the box demanded.
  if (!serving) {
    return {
      error: `Serving size "${form.serving.trim()}" can't be read: write a weight ("7.5 g"), ml ("20 ml") or a count ("1 capsule", "1 sachet (7.5 g)").`,
    };
  }

  // Empty stays null (not 0): a blank fiber means "not entered", and the
  // backend requires the four core values itself.
  const number = (text: string) => (text.trim() === '' ? null : Number(text));
  const body = {
    ...serving,
    calories: number(form.calories),
    proteinGrams: number(form.protein),
    carbohydrateGrams: number(form.carbs),
    fatGrams: number(form.fat),
    fiberGrams: number(form.fiber),
  };
  // A template row left without an amount wasn't on the label: skipped, not
  // sent as an error. A named row with an amount goes to the backend's check.
  const otherRows = form.otherRows
    .filter((r) => r.amount.trim() !== '')
    .map((r) => ({ label: r.label, amount: number(r.amount), unit: r.unit }));
  if (
    Object.values(body).some((v) => typeof v === 'number' && Number.isNaN(v)) ||
    otherRows.some((r) => r.amount !== null && Number.isNaN(r.amount))
  ) {
    return { error: 'Enter numbers only.' };
  }
  return { body: { ...body, otherRows } };
}

// Every field but the product record. The keys come from the form itself, so a
// field added to the form later can't be forgotten here and silently dropped.
function sameInputs(a: ProductDataForm, b: ProductDataForm): boolean {
  return (Object.keys(a) as (keyof ProductDataForm)[]).every((field) => {
    if (field === 'product') return true;
    if (field !== 'otherRows') return a[field] === b[field];
    const x = a.otherRows;
    const y = b.otherRows;
    return (
      x.length === y.length &&
      x.every((r, i) => r.label === y[i].label && r.amount === y[i].amount && r.unit === y[i].unit)
    );
  });
}

/**
 * Unsaved inputs, per product. However the editor closes (a click outside,
 * Esc, Close), inputs that differ from the saved state are kept and come back
 * when the product is reopened.
 *
 * In memory only; a page reload drops them. Deliberately: a draft in browser
 * storage would sit there for days, unaware that the product's saved state
 * changed, and land on top of the new values when opened.
 */
export class DataDrafts {
  private readonly drafts = new Map<number, ProductDataForm>();
  /** The open form's saved state: on close, it decides whether there is a draft. */
  private saved: ProductDataForm | null = null;

  /** When the editor opens: the form built from the saved data, or the product's draft. */
  open(product: AdminProduct): { form: ProductDataForm; draft: boolean; skipped: string[] } {
    const { form, skipped } = storedDataForm(product);
    this.saved = form;
    const draft = this.drafts.get(product.id);
    if (!draft || sameInputs(draft, form)) {
      this.drafts.delete(product.id);
      return { form, draft: false, skipped };
    }
    return { form: { ...draft, product }, draft: true, skipped };
  }

  /**
   * After a successful save, the fields that were sent count as saved. Only
   * those: if nutrition was typed and the category saved, nutrition is still a
   * draft.
   */
  saveSucceeded(sent: ProductDataForm, what: SavedData): void {
    const saved = this.saved;
    if (!saved || saved.product.id !== sent.product.id) return;
    this.saved =
      what === 'category'
        ? { ...saved, category: sent.category }
        : { ...sent, category: saved.category };
  }

  /** When the editor closes: kept as a draft if it differs from the saved state. */
  close(form: ProductDataForm): void {
    if (this.saved && sameInputs(form, this.saved)) this.drafts.delete(form.product.id);
    else this.drafts.set(form.product.id, form);
    this.saved = null;
  }
}
