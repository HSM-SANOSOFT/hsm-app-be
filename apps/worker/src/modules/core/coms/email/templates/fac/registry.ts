import { FacEmailTemplate } from '@hsm-lib/definitions/enums';
import { FacEmailRegistry } from '@hsm-lib/definitions/types';
import { FacturaGeneradaTemplate } from './factura-generada.template';
import { FacturaListaTemplate } from './factura-lista.template';

export const facEmailRegistry: FacEmailRegistry = {
  [FacEmailTemplate.FacturaGenerada]: {
    title: 'Factura Generada',
    subject: 'Su factura ha sido generada',
    template: FacturaGeneradaTemplate,
  },
  [FacEmailTemplate.FacturaLista]: {
    title: 'Factura Lista',
    subject: 'Su factura está lista para descargar',
    template: FacturaListaTemplate,
  },
} satisfies FacEmailRegistry;
