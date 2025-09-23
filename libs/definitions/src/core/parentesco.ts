import type { CodeOf } from '../helper/labelByCode';
import { labelOf } from '../helper/labelByCode';

const PARENTESCO = {
  PADRE: { code: 'PAD', label: 'Padre' },
  MADRE: { code: 'MAD', label: 'Madre' },
  HIJO: { code: 'HIJ', label: 'Hijo/a' },
  CONYUGE: { code: 'CON', label: 'Cónyuge/pareja' },
  HERMANO: { code: 'HER', label: 'Hermano/a' },
  ABUELO: { code: 'ABU', label: 'Abuelo/a' },
  TUTOR: { code: 'TUT', label: 'Tutor/a' },
  AMIGO: { code: 'AMI', label: 'Amigo/a' },
  OTRO: { code: 'OTR', label: 'Otro' },
} as const;
export type Parentesco = CodeOf<typeof PARENTESCO>;
export const ParentescoLabel = labelOf(PARENTESCO);
