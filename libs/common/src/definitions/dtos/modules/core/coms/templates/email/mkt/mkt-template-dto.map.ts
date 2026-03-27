import { MktEmailTemplate } from '@hsm-lib/common/definitions/enums';
import type { DtoClass } from '@hsm-lib/common/definitions/types';

import { CampanaPromocionalTemplateDto } from './campana-promocional.dto';
import { CotizacionServicioTemplateDto } from './cotizacion-servicio.dto';

export const MktTemplateDtoMap = {
  [MktEmailTemplate.CampanaPromocional]: CampanaPromocionalTemplateDto,
  [MktEmailTemplate.CotizacionServicio]: CotizacionServicioTemplateDto,
} as const satisfies Record<MktEmailTemplate, DtoClass>;
