import { MktTemplateDtoMap } from '@hsm-lib/common/definitions/dtos';
import { MktEmailTemplate } from '@hsm-lib/common/definitions/enums';
import { EmailRegistryFromDtoMap } from '@hsm-lib/common/definitions/types';
import { CampanaPromocionalTemplate } from './campana-promocional.template';
import { CotizacionServicioTemplate } from './cotizacion-servicio.template';

export const mktEmailRegistry = {
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
} satisfies EmailRegistryFromDtoMap<typeof MktTemplateDtoMap>;
