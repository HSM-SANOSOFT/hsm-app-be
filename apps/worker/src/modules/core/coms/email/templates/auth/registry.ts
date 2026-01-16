import { AuthEmailTemplate } from '@hsm-lib/definitions/enums';
import { AuthEmailRegistry } from '@hsm-lib/definitions/types';
import { PinInicioSesionTemplate } from './pin-inicio-sesion.template';
import { PinRegistroTemplate } from './pin-registro.template';
import { PinRestablecimientoContrasenaTemplate } from './pin-restablecer-contrasena.template';
import { PinResultadoExamenTemplate } from './pin-resultado-examenes.template';

export const authEmailRegistry: AuthEmailRegistry = {
  [AuthEmailTemplate.PinInicioSesion]: {
    title: 'PIN de Inicio de Sesión',
    subject: 'Tu PIN de Inicio de Sesión',
    template: PinInicioSesionTemplate,
  },
  [AuthEmailTemplate.PinRegistro]: {
    title: 'PIN de Registro',
    subject: 'Tu PIN de Registro',
    template: PinRegistroTemplate,
  },
  [AuthEmailTemplate.PinRestablecerContrasena]: {
    title: 'PIN de Restablecimiento de Contraseña',
    subject: 'Tu PIN de Restablecimiento de Contraseña',
    template: PinRestablecimientoContrasenaTemplate,
  },
  [AuthEmailTemplate.PinResultadoExamen]: {
    title: 'PIN para Resultados de Exámenes',
    subject: 'Tu PIN para Resultados de Exámenes',
    template: PinResultadoExamenTemplate,
  },
} satisfies AuthEmailRegistry;
