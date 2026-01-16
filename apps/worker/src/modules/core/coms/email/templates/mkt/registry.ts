import { MktEmailTemplate } from '@hsm-lib/definitions/enums';
import { MktEmailRegistry } from '@hsm-lib/definitions/types';
import { CampanaPromocionalTemplate } from './campana-promocional.template';
import { CotizacionServicioTemplate } from './cotizacion-servicio.template';

export const mktEmailRegistry: MktEmailRegistry = {
  [MktEmailTemplate.CampanaPromocional]: {
    title: 'Campaña Promocional',
    subject: 'Nueva Campaña Promocional',
    template: CampanaPromocionalTemplate,
  },
  [MktEmailTemplate.CotizacionServicio]: {
    title: 'Cotización de Servicio',
    subject: 'Su Cotización de Servicio está lista',
    template: CotizacionServicioTemplate,
  },
} satisfies MktEmailRegistry;
