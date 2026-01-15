import { MktEmailTemplate } from '@hsm-lib/definitions/enums';
import { MktEmailRegistry } from '@hsm-lib/definitions/types';

export const mktEmailRegistry: MktEmailRegistry = {
  [MktEmailTemplate.CampanaPromocional]: {
    title: 'Campaña Promocional',
    subject: 'Nueva Campaña Promocional',
    template: () => null,
  },
  [MktEmailTemplate.CotizacionServicio]: {
    title: 'Cotización de Servicio',
    subject: 'Su Cotización de Servicio está lista',
    template: () => null,
  },
} satisfies MktEmailRegistry;
