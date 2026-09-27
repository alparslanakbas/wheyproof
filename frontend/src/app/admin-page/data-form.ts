// The admin product data editor's form and its unsaved inputs. A click outside
// the editor closes it, and reopening rebuilt the form from the saved values:
// half-entered input (say, protein and carbs only) was silently lost.

import { AdminProduct } from './admin.service';
import { NutritionRowForm, storedOtherRows } from './nutrition-rows';

export type NutritionField =
  'servingSizeGrams' | 'calories' | 'protein' | 'carbs' | 'fat' | 'fiber';

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

function parseNutrition(json: string | null): Record<string, string> {
  if (!json) return {};
  try {
    return JSON.parse(json) as Record<string, string>;
  } catch {
    return {};
  }
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
      servingSizeGrams: product.servingSizeGrams?.toString() ?? value('Serving Size'),
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
