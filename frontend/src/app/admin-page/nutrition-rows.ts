// Label rows beyond the macros, for the admin nutrition editor. Supplement Facts
// panels (creatine, amino acids, pre-workout, vitamins) print named rows with
// no calories: "Creatine Monohydrate 5 g", "Caffeine 200 mg". The backend
// checks each row (ManualProductDataService.Check); this file only shapes the
// form.

/** The backend's unit list, in the same spelling. */
export const NUTRITION_UNITS = ['g', 'mg', 'mcg', 'IU', 'billion CFU'] as const;

export interface NutritionRowForm {
  label: string;
  /** Kept as text until saved, like the macro inputs. */
  amount: string;
  unit: string;
}

// Rows with their own fields in the editor. The backend refuses them as free
// rows, so they are never offered or read back as one.
const MACRO_LABELS = new Set(['serving size', 'calories', 'total fat', 'total carbohydrate', 'dietary fiber', 'protein']);

export function isMacroLabel(label: string): boolean {
  return MACRO_LABELS.has(label.trim().toLowerCase());
}

type TemplateRow = { label: string; unit: (typeof NUTRITION_UNITS)[number] };

/**
 * The rows a category's labels usually print, offered as a starting point.
 * Amounts are always typed from the label; a template never fills one in.
 */
export const ROW_TEMPLATES: Record<string, TemplateRow[]> = {
  'protein-powder': [
    { label: 'Total Sugars', unit: 'g' },
    { label: 'Sodium', unit: 'mg' },
  ],
  'mass-gainers': [
    { label: 'Total Sugars', unit: 'g' },
    { label: 'Sodium', unit: 'mg' },
  ],
  'protein-snacks': [
    { label: 'Total Sugars', unit: 'g' },
    { label: 'Sodium', unit: 'mg' },
  ],
  creatine: [{ label: 'Creatine Monohydrate', unit: 'g' }],
  'amino-acids': [
    { label: 'L-Leucine', unit: 'g' },
    { label: 'L-Isoleucine', unit: 'g' },
    { label: 'L-Valine', unit: 'g' },
    { label: 'L-Glutamine', unit: 'g' },
  ],
  'pre-workout': [
    { label: 'Caffeine', unit: 'mg' },
    { label: 'L-Citrulline', unit: 'g' },
    { label: 'Beta-Alanine', unit: 'g' },
    { label: 'Betaine Anhydrous', unit: 'g' },
  ],
  'fat-burners': [
    { label: 'Caffeine', unit: 'mg' },
    { label: 'L-Carnitine', unit: 'mg' },
    { label: 'Green Tea Extract', unit: 'mg' },
  ],
  hydration: [
    { label: 'Sodium', unit: 'mg' },
    { label: 'Potassium', unit: 'mg' },
    { label: 'Magnesium', unit: 'mg' },
    { label: 'Calcium', unit: 'mg' },
  ],
  vitamins: [
    { label: 'Vitamin A', unit: 'mcg' },
    { label: 'Vitamin C', unit: 'mg' },
    { label: 'Vitamin D3', unit: 'mcg' },
    { label: 'Vitamin E', unit: 'mg' },
    { label: 'Zinc', unit: 'mg' },
    { label: 'Magnesium', unit: 'mg' },
  ],
};

/** Every template name once, for the row name suggestions. */
export const ROW_NAME_SUGGESTIONS: string[] = [
  ...new Set(Object.values(ROW_TEMPLATES).flatMap((rows) => rows.map((r) => r.label))),
].sort();

// "5g", "25mcg", "3000 IU", "10 billion CFU": the spellings the backend stores.
const STORED_AMOUNT = /^(\d+(?:\.\d+)?)\s*(g|mg|mcg|iu|billion cfu)$/i;

/**
 * Splits a stored table into editable rows. Rows in another shape (an
 * automatic reading's "10%") can't be edited here; they are returned by name
 * so the editor can say they won't be kept on save instead of dropping them
 * silently.
 */
export function storedOtherRows(table: Record<string, string>): { rows: NutritionRowForm[]; skipped: string[] } {
  const rows: NutritionRowForm[] = [];
  const skipped: string[] = [];

  for (const [label, value] of Object.entries(table)) {
    if (isMacroLabel(label)) continue;
    const match = value.trim().match(STORED_AMOUNT);
    const unit = match && NUTRITION_UNITS.find((u) => u.toLowerCase() === match[2].toLowerCase());
    if (match && unit) {
      rows.push({ label, amount: match[1], unit });
    } else {
      skipped.push(label);
    }
  }

  return { rows, skipped };
}

/** The category's template rows not already in the form (by name). */
export function templateRowsToAdd(category: string | null, existing: NutritionRowForm[]): NutritionRowForm[] {
  const names = new Set(existing.map((r) => r.label.trim().toLowerCase()));
  return (ROW_TEMPLATES[category ?? ''] ?? [])
    .filter((t) => !names.has(t.label.toLowerCase()))
    .map((t) => ({ label: t.label, amount: '', unit: t.unit }));
}
