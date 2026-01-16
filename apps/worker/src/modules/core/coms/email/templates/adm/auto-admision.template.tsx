import { AutoAdmisionTemplateDto } from '@hsm-lib/definitions/dtos';
export function AutoAdmisionTemplate(
  data: AutoAdmisionTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>
        Estimad@ paciente: <i>{data.patientName}</i>
        <br />
        <br />
        Le saludamos del Hospital Santamaría.
        <br />
        Hemos registrado a su historia clínica #
        <b>
          <i>{data.documentId}</i>
        </b>
        , un acceso con fecha <i>{data.date}</i>. Que contiene un código de
        verificación necesario identificarse en nuestro sistema.
        <br />
        <br />
        Código de verificación: <b>{data.code}</b>
        <br />
        <br />
        Gracias por su colaboración.
        <br />
        Atentamente,
        <br />
        Admisiones
        <br />
        Servicio Ambulatorio
        <br />
        Celular 0981764904
      </p>
    </>
  );
}
