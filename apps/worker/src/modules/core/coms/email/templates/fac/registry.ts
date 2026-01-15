import { FacEmailTemplate } from '@hsm-lib/definitions/enums';
import { FacEmailRegistry } from '@hsm-lib/definitions/types';

export const facEmailRegistry: FacEmailRegistry = {
  [FacEmailTemplate.FacturaGenerada]: {
    title: 'Factura Generada',
    subject: 'Su factura ha sido generada',
    template: () => null,
  },
  [FacEmailTemplate.FacturaLista]: {
    title: 'Factura Lista',
    subject: 'Su factura está lista para descargar',
    template: () => null,
  },
} satisfies FacEmailRegistry;
