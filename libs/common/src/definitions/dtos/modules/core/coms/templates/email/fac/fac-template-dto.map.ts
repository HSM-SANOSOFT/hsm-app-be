import { FacEmailTemplate } from '@hsm-lib/common/definitions/enums';
import type { DtoClass } from '@hsm-lib/common/definitions/types';

import { FacturaGeneradaTemplateDto } from './factura-generada.dto';
import { FacturaListaTemplateDto } from './factura-lista.dto';

export const FacTemplateDtoMap = {
  [FacEmailTemplate.FacturaGenerada]: FacturaGeneradaTemplateDto,
  [FacEmailTemplate.FacturaLista]: FacturaListaTemplateDto,
} as const satisfies Record<FacEmailTemplate, DtoClass>;
