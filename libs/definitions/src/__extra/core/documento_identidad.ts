import type { CodeOf } from '../helper/labelByCode';
import { labelOf } from '../helper/labelByCode';

export interface IdentificacionItem {
  tipo: DocumentoIdentidad;
  numero: string;
  vigencia?: Date;
}

const DOCUMENTO_IDENTIDAD = {
  CI: { code: 'CDL', label: 'Cédula de Identidad' },
  PASAPORTE: { code: 'PSP', label: 'Pasaporte' },
  RUC: { code: 'RUC', label: 'RUC' },
} as const;
export type DocumentoIdentidad = CodeOf<typeof DOCUMENTO_IDENTIDAD>;
export const DocumentoIdentidadLabel = labelOf(DOCUMENTO_IDENTIDAD);
